using System.Drawing;
using System.Windows.Forms;

namespace VpnReconnect.App;

/// <summary>
/// 托盘图标：窗口关掉后程序仍在后台运行，这里是重新打开窗口的唯一入口。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _openItem;
    private readonly ToolStripMenuItem _checkItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly Dictionary<TrayState, Icon> _icons = new();
    private TrayState _state = (TrayState)(-1);
    private bool? _lastConnected;
    private bool _windowHidden;
    private bool _hiddenHintShown;
    private bool _disposed;

    public TrayIcon()
    {
        _statusItem = new ToolStripMenuItem("当前状态：尚未检查") { Enabled = false };
        _openItem = new ToolStripMenuItem("打开主窗口", null, (_, _) => OnMenuClick(OpenRequested));
        _checkItem = new ToolStripMenuItem("立即检查", null, (_, _) => OnMenuClick(CheckRequested));
        _exitItem = new ToolStripMenuItem("退出程序", null, (_, _) => OnMenuClick(ExitRequested));

        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_openItem);
        menu.Items.Add(_checkItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _icon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Text = "公司 VPN 重连",
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OnMenuClick(OpenRequested);
        _icon.BalloonTipClicked += (_, _) => OnMenuClick(OpenRequested);
        Show(TrayState.Idle);
    }

    public event Action? OpenRequested;

    public event Action? CheckRequested;

    public event Action? ExitRequested;

    /// <summary>窗口当前是否处于隐藏（后台）状态。</summary>
    public bool WindowHidden
    {
        get => _windowHidden;
        set
        {
            _windowHidden = value;
            SyncMenuState();
        }
    }

    public void UpdateStatus(StatusSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        var state = !snapshot.Checked
            ? "尚未检查"
            : snapshot.Connected
                ? "已连接"
                : "未连接";
        var address = string.IsNullOrWhiteSpace(snapshot.AdapterIPv4) ? "" : $" · {snapshot.AdapterIPv4}";
        _icon.Text = Clamp($"公司 VPN · {state}{address}");
        _statusItem.Text = snapshot.Checked ? $"当前状态：{state}{address}" : "当前状态：尚未检查";
        _checkItem.Text = snapshot.Checked ? "立即再检查一次" : "立即检查";

        if (!snapshot.Checked)
        {
            Show(TrayState.Idle);
            _lastConnected = null;
            return;
        }

        if (snapshot.AuthBlocked)
        {
            Show(TrayState.AuthBlocked);
        }
        else
        {
            Show(snapshot.Connected ? TrayState.Connected : TrayState.Disconnected);
        }

        if (WindowHidden)
        {
            NotifyTransition(snapshot.Connected, snapshot);
        }

        _lastConnected = snapshot.Connected;
    }

    public void HintHidden()
    {
        if (_disposed || _hiddenHintShown)
        {
            return;
        }

        _hiddenHintShown = true;
        ShowBalloon("已转到后台继续运行", "点击托盘图标可重新打开窗口，右键可立即检查或退出。", ToolTipIcon.Info);
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _icon.ShowBalloonTip(4000, title, text, icon);
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void SetBusy(bool busy)
    {
        if (_disposed)
        {
            return;
        }

        _checkItem.Enabled = !busy;
        if (busy)
        {
            _statusItem.Text = "正在检查…";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }

        _icons.Clear();
    }

    private static void OnMenuClick(Action? action)
    {
        action?.Invoke();
    }

    private void NotifyTransition(bool connected, StatusSnapshot snapshot)
    {
        if (_lastConnected == connected)
        {
            return;
        }

        if (connected)
        {
            var address = string.IsNullOrWhiteSpace(snapshot.AdapterIPv4) ? "" : $"（{snapshot.AdapterIPv4}）";
            ShowBalloon("公司 VPN 已恢复连接", $"已能访问内网{address}", ToolTipIcon.Info);
        }
        else
        {
            var reason = snapshot.AuthBlocked ? "认证失败，需要保存新密码后才能继续自动重拨" : "正在自动尝试恢复连接";
            ShowBalloon("公司 VPN 已断开", reason, ToolTipIcon.Warning);
        }
    }

    private void SyncMenuState()
    {
        if (_disposed)
        {
            return;
        }

        _openItem.Enabled = WindowHidden;
        _openItem.Text = WindowHidden ? "打开主窗口" : "窗口已打开";
    }

    private void Show(TrayState state)
    {
        if (_state == state)
        {
            return;
        }

        var icon = Load(state);
        if (icon is null)
        {
            return;
        }

        _state = state;
        _icon.Icon = icon;
    }

    private Icon? Load(TrayState state)
    {
        if (_icons.TryGetValue(state, out var cached))
        {
            return cached;
        }

        var name = state switch
        {
            TrayState.Connected => "tray-ok.ico",
            TrayState.Disconnected => "tray-bad.ico",
            TrayState.AuthBlocked => "tray-warn.ico",
            _ => "tray-idle.ico",
        };

        var icon = ReadIcon(name) ?? ReadIcon("app.ico");
        if (icon is not null)
        {
            _icons[state] = icon;
        }

        return icon;
    }

    private static Icon? ReadIcon(string name)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Assets/{name}", UriKind.Absolute);
            var resource = System.Windows.Application.GetResourceStream(uri);
            if (resource is null)
            {
                return null;
            }

            using var stream = resource.Stream;
            return new Icon(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string Clamp(string text)
    {
        return text.Length <= 63 ? text : text[..60] + "…";
    }

    private enum TrayState
    {
        Idle,
        Connected,
        Disconnected,
        AuthBlocked,
    }
}
