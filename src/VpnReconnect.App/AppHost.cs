using System.Windows;
using VpnReconnect.Credentials;
using VpnReconnect.Health;
using VpnReconnect.Logging;
using VpnReconnect.Recovery;
using VpnReconnect.Session;
using VpnReconnect.Settings;
using VpnReconnect.Startup;

namespace VpnReconnect.App;

public sealed class AppHost : IDisposable
{
    private readonly WindowsAdapterLookup _lookup = new();
    private readonly IcmpPing _ping = new();
    private readonly ConnectionLog _log;
    private readonly CredentialStore _store;
    private readonly RecoveryCycle _cycle = new();
    private readonly CheckLogThrottle _throttle = new();
    private readonly SessionController _session = new();
    private readonly WindowsRecoveryIo _io;
    private readonly object _gate = new();
    private AppSettings _settings;
    private CheckService _check;
    private System.Threading.Timer? _timer;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private HealthVerdict? _last;
    private int _busy;
    private bool _disposed;

    public AppHost()
    {
        Directory.CreateDirectory(SettingsFile.DirectoryPath);
        _settings = SettingsFile.Load();
        _log = new ConnectionLog(SettingsFile.LogPath, () => DateTimeOffset.Now);
        _store = new CredentialStore(SettingsFile.CredentialsPath, new DpapiProtector());
        _io = new WindowsRecoveryIo(_settings, _lookup, _ping, _log);
        _check = BuildCheck();
        EnsureShortcut();
    }

    public event Action? ExitRequested;

    public bool HasStoredPassword
    {
        get
        {
            var saved = _store.Load();
            return saved is { Password.Length: > 0 };
        }
    }

    public void Start()
    {
        _session.StartBackground();
        EnsureTray();
        RestartTimer();
        _ = CheckOnceAsync();
    }

    public void RequestShow()
    {
        OnUi(() =>
        {
            var window = EnsureWindow();
            _session.ShowWindow();
            if (_tray is not null)
            {
                _tray.WindowHidden = false;
            }

            window.Reload();
            window.Show();
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
        });
    }

    /// <summary>退出前确认，避免误点导致后台检查停止。</summary>
    public void RequestExit()
    {
        OnUi(() =>
        {
            var owner = _window;
            var text = "退出后将停止定时检查与自动重连，直至下次手动打开。\n确定退出吗？";
            var result = owner is { IsVisible: true }
                ? MessageBox.Show(owner, text, "退出程序", MessageBoxButton.OKCancel, MessageBoxImage.Question)
                : MessageBox.Show(text, "退出程序", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.OK)
            {
                Exit();
            }
        });
    }

    public void Exit()
    {
        StopTimer();
        _session.Exit();
        OnUi(() =>
        {
            if (_window is null)
            {
                return;
            }

            _window.AllowExit = true;
            _window.Close();
            _window = null;
        });
        ExitRequested?.Invoke();
    }

    public void NoteHidden()
    {
        _session.CloseWindow();
        if (_tray is not null)
        {
            _tray.WindowHidden = true;
            _tray.HintHidden();
        }
    }

    public AppSettings CurrentSettings => _settings;

    /// <summary>托盘是否可用；不可用时关闭窗口会直接退出，避免程序躲起来找不到。</summary>
    public bool TrayAvailable => _tray is not null;

    public IReadOnlyList<AdapterSnapshot> ListAdapters()
    {
        return _lookup.ListWithIpv4();
    }

    public StatusSnapshot Snapshot()
    {
        return new StatusSnapshot(
            _last is not null,
            _last?.Connected ?? false,
            _last?.AdapterIPv4,
            ExplainTarget(_settings),
            LatestAction(),
            _cycle.AuthBlocked);
    }

    /// <summary>界面只取最近若干条，日志攒到上万行时也不会拖慢刷新。</summary>
    public IReadOnlyList<LogRow> ReadLog(int maxRows = 500)
    {
        return _log.ReadTail(maxRows);
    }

    public void ExportLog(string path)
    {
        _log.ExportTo(path);
    }

    public void ClearLog()
    {
        _log.Clear();
    }

    public string? Save(AppSettings settings, string passwordFromBox)
    {
        var error = SettingsValidator.Validate(settings);
        if (error is not null)
        {
            return error;
        }

        settings = SettingsValidator.Normalize(settings);
        SettingsFile.Save(settings);
        var existing = _store.Load();
        var password = string.IsNullOrEmpty(passwordFromBox) ? existing?.Password : passwordFromBox;
        if (!string.IsNullOrEmpty(password) || existing is not null)
        {
            try
            {
                Directory.CreateDirectory(SettingsFile.DirectoryPath);
                _store.Save(settings.Username, password ?? "");
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                return $"密码保存失败：{ex.Message}";
            }
            catch (IOException ex)
            {
                return $"密码保存失败：{ex.Message}";
            }
        }

        if (!string.IsNullOrEmpty(passwordFromBox))
        {
            _check.SavePassword(settings.Username, passwordFromBox);
        }

        lock (_gate)
        {
            _settings = settings;
            _io.UseSettings(settings);
            _check = BuildCheck();
        }

        RestartTimer();
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return "找不到当前程序路径，开机启动未更新";
        }

        try
        {
            LoginTaskRegistrar.Apply(LoginTaskSpec.For(exe), settings.StartOnLogin);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        return null;
    }

    public Task CheckOnceAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return Task.CompletedTask;
        }

        _tray?.SetBusy(true);
        return Task.Run(() =>
        {
            try
            {
                CheckService check;
                lock (_gate)
                {
                    check = _check;
                }

                HealthVerdict verdict;
                try
                {
                    verdict = check.CheckOnce();
                }
                catch (Exception ex)
                {
                    _log.Append("检查", "失败", ex.Message, null);
                    verdict = new HealthVerdict(false, ex.Message, null);
                }

                try
                {
                    _log.Rotate(5 * 1024 * 1024, TimeSpan.FromDays(7));
                }
                catch (IOException)
                {
                }

                _last = verdict;
                Publish();
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
                _tray?.SetBusy(false);
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopTimer();
        _tray?.Dispose();
        _tray = null;
        _window = null;
    }

    private void EnsureTray()
    {
        try
        {
            var tray = new TrayIcon();
            tray.OpenRequested += RequestShow;
            tray.CheckRequested += () => _ = CheckOnceAsync();
            tray.ExitRequested += RequestExit;
            _tray = tray;
            tray.UpdateStatus(Snapshot());
            tray.WindowHidden = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _tray = null;
            _log.Append("托盘", "不可用", ex.Message, null);
        }
    }

    private CheckService BuildCheck()
    {
        return new CheckService(
            _settings,
            _lookup,
            _ping,
            _io,
            _cycle,
            _log,
            () => _store.Load(),
            _throttle);
    }

    private void RestartTimer()
    {
        StopTimer();
        var seconds = _settings.IntervalSeconds is >= 15 and <= 600 ? _settings.IntervalSeconds : 60;
        var interval = TimeSpan.FromSeconds(seconds);
        _timer = new System.Threading.Timer(_ => _ = CheckOnceAsync(), null, interval, interval);
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private MainWindow EnsureWindow()
    {
        if (_window is not null)
        {
            return _window;
        }

        _window = new MainWindow(this);
        return _window;
    }

    private void Publish()
    {
        _tray?.UpdateStatus(Snapshot());
        if (_window is null)
        {
            return;
        }

        OnUi(() => _window?.RefreshLive());
    }

    private string LatestAction()
    {
        var rows = _log.ReadTail(1);
        if (rows.Count == 0)
        {
            return "尚未检查";
        }

        var last = rows[^1];
        return $"{last.Time.LocalDateTime:yyyy-MM-dd HH:mm:ss} {last.Step} {last.Result} {last.Evidence}";
    }

    private static string ExplainTarget(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.TargetAddress))
        {
            return "未设置目标地址，按所选适配器是否有有效 IPv4 判断";
        }

        return $"目标地址 {settings.TargetAddress.Trim()}，以 ping 为准";
    }

    private static void EnsureShortcut()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        try
        {
            var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
            Directory.CreateDirectory(programs);
            var link = Path.Combine(programs, "公司VPN重连.lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = exe;
            shortcut.Arguments = "";
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe) ?? "";
            shortcut.Description = "打开公司 VPN 重连窗口";
            shortcut.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
        }
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}

public readonly record struct StatusSnapshot(
    bool Checked,
    bool Connected,
    string? AdapterIPv4,
    string TargetExplanation,
    string Recent,
    bool AuthBlocked);
