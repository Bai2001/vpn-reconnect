using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using VpnReconnect.Health;
using VpnReconnect.Logging;
using VpnReconnect.Recovery;
using VpnReconnect.Session;
using VpnReconnect.Settings;

namespace VpnReconnect.App;

public sealed class WindowsRecoveryIo : IRecoveryIo
{
    private const int DialParamsSize = 2120;
    private const int OffsetEntry = 4;
    private const int OffsetUser = 1034;
    private const int OffsetPassword = 1548;
    private const int RasConnSize = 1388;
    private const int RasConnHandleOffset = 4;
    private const int RasStatusSize = 608;
    private const int ErrorBufferTooSmall = 603;
    private const int ErrorAuth = 691;
    private const int StatePaused = 0x1000;
    private const int StateConnected = 0x2000;
    private const int StateDisconnected = 0x2001;
    private const uint NotifierCallback = 1;
    private const string L2tpInstanceId = @"SWD\MSRRAS\MS_L2TPMINIPORT";
    private const string NdisWanIpId = @"SWD\MSRRAS\MS_NDISWANIP";
    private const string NdisWanIpv6Id = @"SWD\MSRRAS\MS_NDISWANIPV6";

    private readonly IAdapterLookup _lookup;
    private readonly IPing _ping;
    private readonly ConnectionLog _log;
    private readonly ManualResetEventSlim _dialDone = new(false);
    private AppSettings _settings;
    private RasDialCallback? _dialCallback;
    private int _dialState;
    private uint _dialError;

    public WindowsRecoveryIo(AppSettings settings, IAdapterLookup lookup, IPing ping, ConnectionLog log)
    {
        _settings = settings;
        _lookup = lookup;
        _ping = ping;
        _log = log;
    }

    public void UseSettings(AppSettings settings)
    {
        Volatile.Write(ref _settings, settings);
    }

    public bool IsHealthy()
    {
        var settings = Volatile.Read(ref _settings);
        var adapter = string.IsNullOrWhiteSpace(settings.SelectedAdapterName)
            ? null
            : _lookup.Get(settings.SelectedAdapterName);
        return ConnectivityJudge.Judge(
            settings.TargetAddress,
            settings.SelectedAdapterName,
            adapter,
            _ping).Connected;
    }

    public bool HasResidualDial()
    {
        try
        {
            return EnumHandles().Count > 0 || RasdialProcessExists();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            return RasdialProcessExists();
        }
    }

    public void KillResidualAndDisconnect()
    {
        foreach (var handle in EnumHandles())
        {
            HangUp(handle);
        }

        KillRasdialProcesses();
    }

    public DialReport Dial(string connectionName, string username, string password)
    {
        var submitted = !string.IsNullOrWhiteSpace(username);
        var buffer = AllocDialParams(connectionName ?? "", username ?? "", password ?? "");
        try
        {
            _dialDone.Reset();
            _dialState = 0;
            _dialError = 0;
            _dialCallback = OnDialState;
            var started = RasDialW(IntPtr.Zero, IntPtr.Zero, buffer, NotifierCallback, _dialCallback, out var handle);
            if (started != 0)
            {
                return ToReport(started, submitted);
            }

            if (!_dialDone.Wait(TimeSpan.FromSeconds(30)))
            {
                HangUp(handle);
                return new DialReport(DialFinish.Hung, null, submitted);
            }

            if (_dialError == 0 && _dialState == StateConnected)
            {
                return new DialReport(DialFinish.Up, 0, submitted);
            }

            HangUp(handle);
            var error = _dialError == 0 ? 628u : _dialError;
            return ToReport(error, submitted);
        }
        finally
        {
            Wipe(buffer, OffsetPassword, 257 * 2);
            Marshal.FreeHGlobal(buffer);
            _dialCallback = null;
        }
    }

    public bool L2tpMiniportPresent()
    {
        return DevicePresent(L2tpInstanceId);
    }

    public void RestartRasManAndIkeext()
    {
        RestartService("RasMan");
        RestartService("IKEEXT");
    }

    public void RestartL2tpMiniport()
    {
        RestartDevice(L2tpInstanceId);
    }

    public void RestartIpMiniports()
    {
        RestartDevice(NdisWanIpId);
        RestartDevice(NdisWanIpv6Id);
    }

    private uint OnDialState(IntPtr handle, uint message, int state, uint error, uint extended)
    {
        _ = handle;
        _ = message;
        _ = extended;
        _dialError = error;
        _dialState = state;
        if (error != 0 || state == StateConnected || state == StateDisconnected)
        {
            _dialDone.Set();
        }

        return 1;
    }

    private static DialReport ToReport(uint error, bool submitted)
    {
        if (error == ErrorAuth)
        {
            return new DialReport(DialFinish.AuthRejected, ErrorAuth, submitted);
        }

        if (error == 0)
        {
            return new DialReport(DialFinish.Up, 0, submitted);
        }

        return new DialReport(DialFinish.Failed, (int)error, submitted);
    }

    private void RestartService(string name)
    {
        try
        {
            using var service = new ServiceController(name);
            var dependents = service.DependentServices
                .Where(item => item.Status is not ServiceControllerStatus.Stopped and not ServiceControllerStatus.StopPending)
                .Select(item => item.ServiceName)
                .ToArray();
            foreach (var dependent in service.DependentServices)
            {
                try
                {
                    if (dependent.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
                    {
                        dependent.Stop();
                        dependent.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(40));
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.TimeoutException or Win32Exception)
                {
                    _log.Append("重启服务", "失败", $"{dependent.ServiceName} 未能停止：{ex.Message}", null);
                }
                finally
                {
                    dependent.Dispose();
                }
            }

            service.Refresh();
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(40));
            }

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(40));
            foreach (var dependentName in dependents)
            {
                try
                {
                    using var dependent = new ServiceController(dependentName);
                    if (dependent.Status == ServiceControllerStatus.Stopped)
                    {
                        dependent.Start();
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    _log.Append("重启服务", "失败", $"{dependentName} 未能重新启动：{ex.Message}", null);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.TimeoutException or Win32Exception)
        {
            _log.Append("重启服务", "失败", $"{name} 重启失败：{ex.Message}", null);
        }
    }

    private void RestartDevice(string instanceId)
    {
        var code = RunPnputil(out _, "/restart-device", instanceId);
        if (code != 0)
        {
            _log.Append("重启网卡", "失败", $"{instanceId} 重启失败，退出码 {code}", null);
        }
    }

    private static bool DevicePresent(string instanceId)
    {
        var code = RunPnputil(out var output, "/enum-devices", "/instanceid", instanceId);
        return code == 0 && output.Contains(instanceId, StringComparison.OrdinalIgnoreCase);
    }

    private static int RunPnputil(out string output, params string[] args)
    {
        output = "";
        using var process = new Process();
        process.StartInfo.FileName = Path.Combine(Environment.SystemDirectory, "pnputil.exe");
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return -1;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(60_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Win32Exception)
            {
            }

            return -1;
        }

        output = stdout + stderr;
        return process.ExitCode;
    }

    private static IntPtr AllocDialParams(string entry, string username, string password)
    {
        var buffer = Marshal.AllocHGlobal(DialParamsSize);
        var zeros = new byte[DialParamsSize];
        Marshal.Copy(zeros, 0, buffer, zeros.Length);
        Marshal.WriteInt32(buffer, DialParamsSize);
        WriteUtf16(buffer, OffsetEntry, 257, entry);
        WriteUtf16(buffer, OffsetUser, 257, username);
        WriteUtf16(buffer, OffsetPassword, 257, password);
        return buffer;
    }

    private static void WriteUtf16(IntPtr buffer, int offset, int charCapacity, string value)
    {
        if (string.IsNullOrEmpty(value) || charCapacity <= 1)
        {
            return;
        }

        var chars = Math.Min(value.Length, charCapacity - 1);
        var bytes = Encoding.Unicode.GetBytes(value.AsSpan(0, chars).ToArray());
        Marshal.Copy(bytes, 0, buffer + offset, bytes.Length);
    }

    private static void Wipe(IntPtr buffer, int offset, int bytes)
    {
        var zeros = new byte[bytes];
        Marshal.Copy(zeros, 0, buffer + offset, bytes);
    }

    private static void HangUp(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        RasHangUpW(handle);
        var status = Marshal.AllocHGlobal(RasStatusSize);
        try
        {
            for (var i = 0; i < 50; i++)
            {
                Marshal.WriteInt32(status, RasStatusSize);
                if (RasGetConnectStatusW(handle, status) != 0)
                {
                    return;
                }

                Thread.Sleep(100);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(status);
        }
    }

    private static List<IntPtr> EnumHandles()
    {
        var handles = new List<IntPtr>();
        var bytes = RasConnSize;
        var buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            Marshal.WriteInt32(buffer, RasConnSize);
            var count = 0;
            var result = RasEnumConnectionsW(buffer, ref bytes, ref count);
            if (result == ErrorBufferTooSmall)
            {
                if (bytes <= 0 || bytes > 1024 * 1024)
                {
                    return handles;
                }

                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
                buffer = Marshal.AllocHGlobal(bytes);
                for (var offset = 0; offset + RasConnSize <= bytes; offset += RasConnSize)
                {
                    Marshal.WriteInt32(buffer, offset, RasConnSize);
                }

                result = RasEnumConnectionsW(buffer, ref bytes, ref count);
            }

            if (result != 0 || count <= 0)
            {
                return handles;
            }

            for (var i = 0; i < count; i++)
            {
                handles.Add(Marshal.ReadIntPtr(buffer, i * RasConnSize + RasConnHandleOffset));
            }

            return handles;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool RasdialProcessExists()
    {
        var processes = Process.GetProcessesByName("rasdial");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static void KillRasdialProcesses()
    {
        foreach (var process in Process.GetProcessesByName("rasdial"))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint RasDialCallback(IntPtr handle, uint unMsg, int rascs, uint dwError, uint extendedError);

    [DllImport("rasapi32.dll", EntryPoint = "RasDialW", CharSet = CharSet.Unicode)]
    private static extern uint RasDialW(
        IntPtr extensions,
        IntPtr phonebook,
        IntPtr dialParams,
        uint notifierType,
        RasDialCallback notifier,
        out IntPtr handle);

    [DllImport("rasapi32.dll", EntryPoint = "RasHangUpW")]
    private static extern uint RasHangUpW(IntPtr handle);

    [DllImport("rasapi32.dll", EntryPoint = "RasGetConnectStatusW")]
    private static extern uint RasGetConnectStatusW(IntPtr handle, IntPtr status);

    [DllImport("rasapi32.dll", EntryPoint = "RasEnumConnectionsW", CharSet = CharSet.Unicode)]
    private static extern uint RasEnumConnectionsW(IntPtr connections, ref int bytes, ref int count);
}
