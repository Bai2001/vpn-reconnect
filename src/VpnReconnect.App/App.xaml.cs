using System.Runtime.InteropServices;
using System.Windows;
using VpnReconnect.Startup;

namespace VpnReconnect.App;

public partial class App : Application
{
    private SingleInstance? _gate;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var background = StartupArgs.IsBackground(e.Args);

        // Windows 重启/更新后自动重开本程序时，也走静默参数，避免登录后又弹窗
        RegisterRestart();

        if (!SingleInstance.TryBecomePrimary(out _gate))
        {
            // 只有用户主动再打开一次才唤出窗口；静默启动的第二份不要弹窗
            if (!background)
            {
                SingleInstance.RequestShow();
            }

            Shutdown();
            return;
        }

        _host = new AppHost();
        _gate!.ShowRequested += (_, _) => _host.RequestShow();
        _host.ExitRequested += Shutdown;
        _host.Start();
        if (!background)
        {
            _host.RequestShow();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _gate?.Dispose();
        base.OnExit(e);
    }

    private static void RegisterRestart()
    {
        try
        {
            _ = RegisterApplicationRestart(StartupArgs.Background, 0);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegisterApplicationRestart(string commandLine, uint flags);
}
