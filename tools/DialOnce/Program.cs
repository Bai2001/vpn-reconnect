using System.Runtime.InteropServices;
using VpnReconnect.App;
using VpnReconnect.Credentials;

var credPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "VpnReconnect",
    "credentials.bin");
var phonebook = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Microsoft",
    "Network",
    "Connections",
    "Pbk",
    "rasphone.pbk");
var saved = new CredentialStore(credPath, new DpapiProtector()).Load();
if (saved is null || string.IsNullOrWhiteSpace(saved.Value.Username) || string.IsNullOrWhiteSpace(saved.Value.Password))
{
    Console.WriteLine("没有读到已保存的用户名和密码，不拨号。");
    return 2;
}

Console.WriteLine($"用户名长度={saved.Value.Username.Length} 密码已保存={saved.Value.Password.Length > 0} 电话簿存在={File.Exists(phonebook)}");
Console.WriteLine($"RASDIALPARAMS 大小={Marshal.SizeOf<RasDialParams>()}");

var dial = new IsolatedDial();
var report = dial.Run("公司", saved.Value.Username, saved.Value.Password, phonebook);
Console.WriteLine($"结果={report.Finish} 错误码={report.Error?.ToString() ?? "无"} 最终状态={report.State} {report.StateName}");
foreach (var line in report.Trace)
{
    Console.WriteLine(line);
}

return report.Finish == "已连接" ? 0 : 1;

internal sealed class IsolatedDial
{
    private readonly ManualResetEventSlim _done = new(false);
    private readonly List<string> _trace = [];
    private RasDialFunc? _callback;
    private int _state;
    private uint _error;

    public DialRunResult Run(string entry, string username, string password, string phonebook)
    {
        var parms = new RasDialParams
        {
            dwSize = Marshal.SizeOf<RasDialParams>(),
            szEntryName = entry,
            szUserName = username,
            szPassword = password,
        };
        _callback = OnState;
        var started = Native.RasDialW(IntPtr.Zero, phonebook, ref parms, 1, _callback, out var handle);
        _trace.Add($"RasDialW 返回={started} 句柄={handle}");
        if (started != 0)
        {
            return Finish("启动失败", started, handle);
        }

        if (!_done.Wait(TimeSpan.FromSeconds(40)))
        {
            Native.RasHangUpW(handle);
            return Finish("40秒无结果", null, handle);
        }

        if (_error == 0 && _state == 0x2000)
        {
            return Finish("已连接", 0, handle);
        }

        Native.RasHangUpW(handle);
        return Finish("失败", _error == 0 ? 628 : _error, handle);
    }

    private DialRunResult Finish(string finish, uint? error, IntPtr handle)
    {
        return new DialRunResult(finish, error, _state, StateName(_state), _trace);
    }

    private uint OnState(IntPtr handle, uint message, int state, uint error, uint extended)
    {
        _state = state;
        _error = error;
        _trace.Add($"回调 句柄={handle} 消息={message} 状态={state} {StateName(state)} 错误={error} 扩展={extended}");
        if (error != 0 || state is 0x2000 or 0x2001)
        {
            _done.Set();
        }

        return 1;
    }

    private static string StateName(int state)
    {
        return state switch
        {
            0 => "打开端口",
            1 => "端口已打开",
            2 => "连接设备",
            3 => "设备已连接",
            4 => "全部设备已连接",
            5 => "验证身份",
            0x1000 => "暂停等待交互",
            0x2000 => "已连接",
            0x2001 => "已断开",
            _ => "其他",
        };
    }
}

internal sealed record DialRunResult(string Finish, uint? Error, int State, string StateName, IReadOnlyList<string> Trace);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct RasDialParams
{
    public int dwSize;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
    public string szEntryName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)]
    public string szPhoneNumber;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)]
    public string szCallbackNumber;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
    public string szUserName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
    public string szPassword;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string szDomain;
    public int dwSubEntry;
    public IntPtr dwCallbackId;
    public int dwIfIndex;
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate uint RasDialFunc(IntPtr handle, uint message, int state, uint error, uint extended);

internal static class Native
{
    [DllImport("rasapi32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RasDialW(
        IntPtr extensions,
        string phonebook,
        ref RasDialParams dialParams,
        uint notifierType,
        RasDialFunc notifier,
        out IntPtr handle);

    [DllImport("rasapi32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RasHangUpW(IntPtr handle);
}
