using System.Windows;
using VpnReconnect.Credentials;
using VpnReconnect.Health;
using VpnReconnect.Logging;
using VpnReconnect.Recovery;
using VpnReconnect.Session;
using VpnReconnect.Settings;
using VpnReconnect.Startup;
using VpnReconnect.Updates;

namespace VpnReconnect.App;

public sealed class AppHost : IDisposable
{
    private readonly WindowsAdapterLookup _lookup = new();
    private readonly IcmpPing _ping = new();
    private readonly ConnectionLog _log;
    private readonly CredentialStore _store;
    private readonly RecoveryCycle _cycle = new();
    private readonly CheckLogThrottle _throttle = new();
    private readonly UpdateCoordinator _updates;
    private readonly SessionController _session = new();
    private readonly WindowsRecoveryIo _io;
    private readonly object _gate = new();
    private AppSettings _settings;
    private CheckService _check;
    private System.Threading.Timer? _timer;
    private System.Threading.Timer? _updateTimer;
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
        _updates = new UpdateCoordinator(
            _log,
            CurrentVersion,
            Path.Combine(SettingsFile.DirectoryPath, "update"));
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
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exe))
        {
            _ = Task.Run(() => CleanupBackup(exe));
        }

        EnsureTray();
        RestartTimer();
        RestartUpdateTimer();
        _ = CheckOnceAsync();
        if (_settings.AutoCheckUpdates)
        {
            _ = CheckUpdatesAsync(manual: false);
        }
    }

    public UpdateState UpdateSnapshot => _updates.Snapshot;

    /// <summary>重启前用它主动释放单实例锁，避免新进程抢不到锁直接退出。</summary>
    public Action? ReleaseInstanceForRestart { get; set; }

    /// <summary>查最新版本；有更新时弹托盘气泡提示。</summary>
    public async Task CheckUpdatesAsync(bool manual)
    {
        var ok = await _updates.CheckAsync(manual, CancellationToken.None).ConfigureAwait(false);
        _ = ok;
        var state = _updates.Snapshot;
        _tray?.SetUpdateAvailable(state.Stage == UpdateStage.Available ? state.LatestVersion : null);
        if (state.Stage == UpdateStage.Available)
        {
            _tray?.ShowBalloon(
                $"发现新版本 v{state.LatestVersion}",
                state.CanInstall ? "打开主窗口点「立即更新」就能升级。" : "请到 Release 页面手动下载。",
                System.Windows.Forms.ToolTipIcon.Info);
        }

        Publish();
    }

    /// <summary>托盘里点「检查更新」：查到新版本就把主窗口叫出来，让用户决定装不装。</summary>
    private async Task CheckUpdatesFromTrayAsync()
    {
        await CheckUpdatesAsync(manual: true).ConfigureAwait(false);
        if (_updates.Snapshot.Stage == UpdateStage.Available)
        {
            RequestShow();
        }
    }

    /// <summary>下载、校验并替换程序文件，然后重启到新版本。</summary>
    public async Task<bool> ApplyUpdateAsync(bool windowVisible)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            _updates.MarkFailed("找不到当前程序路径，无法自动更新，请到 Release 页面手动下载");
            Publish();
            return false;
        }

        var prepared = await _updates.PrepareAsync(CancellationToken.None).ConfigureAwait(false);
        if (prepared is null)
        {
            Publish();
            return false;
        }

        var version = _updates.Snapshot.LatestVersion ?? "新版本";
        try
        {
            UpdateSwapper.Replace(exe, prepared);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _updates.MarkFailed($"替换程序文件失败：{ex.Message}。可到 Release 页面手动下载");
            _log.Append("更新", "替换失败", ex.Message, null);
            Publish();
            return false;
        }

        _log.Append("更新", "已替换", $"已更新到 v{version}，准备重启", null);
        _updates.MarkRestarting(version);
        Publish();
        RestartProcess(exe, windowVisible);
        return true;
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

    public SaveOutcome Save(AppSettings settings, string passwordFromBox)
    {
        var error = SettingsValidator.Validate(settings);
        if (error is not null)
        {
            return new SaveOutcome(error, null);
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
                return new SaveOutcome($"密码保存失败：{ex.Message}", null);
            }
            catch (IOException ex)
            {
                return new SaveOutcome($"密码保存失败：{ex.Message}", null);
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
            return new SaveOutcome("已保存，但找不到当前程序路径，开机启动未更新", null);
        }

        return ApplyAutoStart(exe, settings.StartOnLogin);
    }

    /// <summary>当前权限与开机启动方式，显示在设置页给用户核对。</summary>
    public string StartupSummary()
    {
        var parts = new List<string>
        {
            IsElevated() ? "当前权限：管理员" : "当前权限：普通用户",
        };

        if (LoginTaskRegistrar.Exists())
        {
            parts.Add("开机启动：计划任务（最高权限）");
        }
        else if (RunKeyRegistrar.Exists())
        {
            parts.Add("开机启动：注册表自启（普通权限）");
        }
        else
        {
            parts.Add("开机启动：未启用");
        }

        return string.Join(" · ", parts);
    }

    private static SaveOutcome ApplyAutoStart(string exePath, bool enabled)
    {
        var elevated = IsElevated();
        var taskExists = LoginTaskRegistrar.Exists();

        // 已有高权限计划任务但当前没提权：保持现状，别重复写注册表自启（会双启动）
        if (enabled && taskExists && !elevated)
        {
            RunKeyRegistrar.Remove();
            var registered = LoginTaskRegistrar.RegisteredExePath();
            var same = !string.IsNullOrWhiteSpace(registered)
                && string.Equals(registered, exePath, StringComparison.OrdinalIgnoreCase);
            return same
                ? new SaveOutcome(null, "开机启动：已由现有计划任务负责（最高权限），本次无需改动")
                : new SaveOutcome(
                    null,
                    $"开机启动：现有计划任务指向 {registered}，与当前程序路径不同。请右键以管理员身份运行本程序后再保存一次以更新。");
        }

        var mode = AutoStartPlanner.Decide(enabled, elevated, taskExists, IsDebugBuild());
        if (mode == AutoStartMode.SkippedDebugBuild)
        {
            return new SaveOutcome(null, SkipDebugBuildAutostart(exePath));
        }

        if (mode == AutoStartMode.Disabled)
        {
            var deleteError = LoginTaskRegistrar.Apply(LoginTaskSpec.For(exePath), false);
            RunKeyRegistrar.Remove();
            return deleteError is null
                ? new SaveOutcome(null, "已关闭开机启动")
                : new SaveOutcome(deleteError, null);
        }

        if (mode == AutoStartMode.ScheduledTask)
        {
            var failure = LoginTaskRegistrar.Apply(LoginTaskSpec.For(exePath), true);
            if (failure is null)
            {
                RunKeyRegistrar.Remove();
                return new SaveOutcome(null, "开机启动：已注册计划任务（最高权限，断线时能重启服务）");
            }

            try
            {
                RunKeyRegistrar.Apply(exePath);
                return new SaveOutcome(
                    null,
                    $"计划任务注册失败（{failure}），已改用注册表自启动。该方式以普通权限运行，断线时重启服务会被拒绝；需要完整恢复能力请右键以管理员身份运行本程序后再保存一次。");
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return new SaveOutcome($"开机启动注册失败：{failure}；注册表自启动也失败：{ex.Message}", null);
            }
        }

        try
        {
            RunKeyRegistrar.Apply(exePath);
            return new SaveOutcome(
                null,
                "开机启动：已设为当前用户自启动（注册表，普通权限）。普通权限下断线时无法重启服务，如需完整恢复能力，请右键以管理员身份运行本程序后再保存一次。");
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return new SaveOutcome($"开机启动设置失败：{ex.Message}", null);
        }
    }

    /// <summary>
    /// 调试构建不写开机启动，并把指向本 exe 的旧自启动顺手清掉，
    /// 避免开发调试时把 bin\Debug 的 exe 注册成开机启动。
    /// </summary>
    private static string SkipDebugBuildAutostart(string exePath)
    {
        var cleaned = new List<string>();
        try
        {
            if (SamePath(LoginTaskRegistrar.RegisteredExePath(), exePath)
                && LoginTaskRegistrar.Apply(LoginTaskSpec.For(exePath), false) is null)
            {
                cleaned.Add("计划任务");
            }

            if (SamePath(RunKeyRegistrar.RegisteredPath(), exePath))
            {
                RunKeyRegistrar.Remove();
                cleaned.Add("注册表自启");
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _ = ex;
        }

        var suffix = cleaned.Count == 0 ? "" : $"已清理指向本调试版的{string.Join("、", cleaned)}。";
        return $"当前是调试版本，不注册开机启动（避免把 bin\\Debug 的 exe 设成自启动）。{suffix}请用 Release 版本设置开机启动。";
    }

    private static bool SamePath(string? registered, string exePath)
    {
        return !string.IsNullOrWhiteSpace(registered)
            && string.Equals(registered, exePath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
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
        StopUpdateTimer();
        _updates.Dispose();
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
            tray.UpdateCheckRequested += () => _ = CheckUpdatesFromTrayAsync();
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

    /// <summary>上次更新留下的 .old 要等旧进程退干净才能删，失败就多试几次。</summary>
    private static void CleanupBackup(string exePath)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (UpdateSwapper.TryCleanupBackup(exePath))
            {
                break;
            }

            Thread.Sleep(1000);
        }

        UpdateCoordinator.PurgeOldFiles(Path.Combine(SettingsFile.DirectoryPath, "update"));
    }

    private void RestartUpdateTimer()
    {
        StopUpdateTimer();
        var interval = TimeSpan.FromHours(24);
        _updateTimer = new System.Threading.Timer(
            _ =>
            {
                if (!_disposed)
                {
                    _ = CheckUpdatesAsync(manual: false);
                }
            },
            null,
            interval,
            interval);
    }

    private void StopUpdateTimer()
    {
        _updateTimer?.Dispose();
        _updateTimer = null;
    }

    private static ReleaseVersion CurrentVersion()
    {
        var version = typeof(AppHost).Assembly.GetName().Version;
        return version is null
            ? new ReleaseVersion(0, 0, 0, false)
            : ReleaseVersion.FromSystem(version);
    }

    private void RestartProcess(string exePath, bool windowVisible)
    {
        var arguments = windowVisible
            ? StartupArgs.Takeover
            : $"{StartupArgs.Takeover} {StartupArgs.Background}";

        // 先自己放开单实例锁再拉新进程，否则新进程（尤其是不认识 --takeover 的旧版本）
        // 会因为抢不到锁而直接退出，表现为更新完程序就不见了
        ReleaseInstanceForRestart?.Invoke();
        Thread.Sleep(300);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath, arguments)
            {
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? ".",
            });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _updates.MarkFailed($"重启新版本失败（{ex.Message}），请手动打开程序");
            Publish();
            return;
        }

        Exit();
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

public readonly record struct SaveOutcome(string? Error, string? Note);

public readonly record struct StatusSnapshot(
    bool Checked,
    bool Connected,
    string? AdapterIPv4,
    string TargetExplanation,
    string Recent,
    bool AuthBlocked);
