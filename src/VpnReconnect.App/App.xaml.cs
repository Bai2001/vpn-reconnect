using System.Windows;

namespace VpnReconnect.App;

public partial class App : Application
{
    private SingleInstance? _gate;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var background = e.Args.Any(arg => string.Equals(arg, "--background", StringComparison.OrdinalIgnoreCase));
        if (!SingleInstance.TryBecomePrimary(out _gate))
        {
            SingleInstance.RequestShow();
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
}
