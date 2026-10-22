using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using VpnReconnect.Logging;
using VpnReconnect.Settings;

namespace VpnReconnect.App;

public partial class MainWindow : Window
{
    private static readonly Brush OkBrush = Freeze(Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly Brush BadBrush = Freeze(Color.FromRgb(0xDC, 0x26, 0x26));
    private static readonly Brush WarnBrush = Freeze(Color.FromRgb(0xD9, 0x77, 0x06));
    private static readonly Brush IdleBrush = Freeze(Color.FromRgb(0x9C, 0xA3, 0xAF));
    private static readonly Brush OkBadgeBrush = Freeze(Color.FromRgb(0xE9, 0xF8, 0xEF));
    private static readonly Brush BadBadgeBrush = Freeze(Color.FromRgb(0xFD, 0xEC, 0xEC));
    private static readonly Brush WarnBadgeBrush = Freeze(Color.FromRgb(0xFE, 0xF3, 0xE2));
    private static readonly Brush IdleBadgeBrush = Freeze(Color.FromRgb(0xF3, 0xF4, 0xF6));

    private const int LogPageSize = 500;

    private readonly AppHost _host;
    private IReadOnlyList<LogRow> _logRows = Array.Empty<LogRow>();
    private string? _keptAdapterName;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Reload();
    }

    public bool AllowExit { get; set; }

    /// <summary>重新载入设置表单与状态、日志（打开窗口、保存之后调用）。</summary>
    public void Reload()
    {
        var settings = _host.CurrentSettings;
        _keptAdapterName = settings.SelectedAdapterName ?? settings.ConnectionName;
        TargetBox.Text = settings.TargetAddress ?? "";
        UsernameBox.Text = settings.Username ?? "";
        IntervalBox.Text = settings.IntervalSeconds.ToString();
        StartOnLoginBox.IsChecked = settings.StartOnLogin;
        PasswordBox.Clear();
        PasswordHint.Text = _host.HasStoredPassword
            ? "已保存密码。密码框不回显已存密码，留空则不修改"
            : "尚未保存密码。填写后点保存，密码会用当前 Windows 账号加密保存";
        FillAdapters(settings.SelectedAdapterName);
        RefreshLive();
    }

    /// <summary>只刷新状态与日志，不覆盖正在编辑的设置表单。</summary>
    public void RefreshLive()
    {
        _logRows = _host.ReadLog(LogPageSize);
        ShowStatus(_host.Snapshot(), _logRows, _host.CurrentSettings.IntervalSeconds);
        ShowLog();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowExit)
        {
            if (!_host.TrayAvailable)
            {
                // 没有托盘图标时躲到后台会找不到窗口，直接退出更安全
                _host.Exit();
                return;
            }

            e.Cancel = true;
            Hide();
            _host.NoteHidden();
            return;
        }

        base.OnClosing(e);
    }

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        RecentStepText.Text = "正在检查…";
        RecentEvidenceText.Text = "已提交一次手动检查";
        CheckNowButton.IsEnabled = false;
        try
        {
            await _host.CheckOnceAsync();
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
            RefreshLive();
        }
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _host.NoteHidden();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _host.RequestExit();
    }

    private void RefreshAdapters_Click(object sender, RoutedEventArgs e)
    {
        var selected = AdapterList.SelectedItem is AdapterRow row ? row.Name : _keptAdapterName;
        FillAdapters(selected);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var interval = int.TryParse(IntervalBox.Text.Trim(), out var seconds) ? seconds : 0;
        var adapterName = ChosenAdapter();
        if (string.IsNullOrWhiteSpace(adapterName))
        {
            SetMessage("请先在列表中选择适配器。断线时该接口会从列表消失，请在它有地址时选择。", isError: true);
            return;
        }

        var settings = new AppSettings(
            adapterName,
            string.IsNullOrWhiteSpace(TargetBox.Text) ? null : TargetBox.Text,
            adapterName,
            UsernameBox.Text,
            interval,
            StartOnLoginBox.IsChecked == true);
        var password = PasswordBox.Password;
        var error = _host.Save(settings, password);
        PasswordBox.Clear();
        Reload();
        if (error is null)
        {
            SetMessage("已保存。新的设置已生效。", isError: false);
            return;
        }

        SetMessage(error, isError: true);
    }

    private void RefreshLog_Click(object sender, RoutedEventArgs e)
    {
        RefreshLive();
    }

    private void OnlyProblems_Changed(object sender, RoutedEventArgs e)
    {
        ShowLog();
    }

    private void ExportLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "日志文件|*.log|文本文件|*.txt",
            FileName = "公司VPN重连日志.log",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _host.ExportLog(dialog.FileName);
        MessageBox.Show(this, "已导出日志", "导出", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "确定清空日志？", "清空", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        _host.ClearLog();
        RefreshLive();
    }

    private void FillAdapters(string? selectedName)
    {
        var rows = _host.ListAdapters()
            .Select(item => new AdapterRow(item.ConnectionName, item.IPv4))
            .ToList();
        // 已保存的适配器排到最上面，避免选中项被滚动条藏在列表底部看不见
        var ordered = rows
            .OrderBy(item => string.Equals(item.Name, selectedName, StringComparison.Ordinal) ? 0 : 1)
            .ToList();
        AdapterList.ItemsSource = ordered;
        AdapterList.SelectedItem = ordered.FirstOrDefault(item => string.Equals(item.Name, selectedName, StringComparison.Ordinal));
    }

    private string? ChosenAdapter()
    {
        if (AdapterList.SelectedItem is AdapterRow row)
        {
            return row.Name;
        }

        return _keptAdapterName;
    }

    private void SetMessage(string text, bool isError)
    {
        SettingsMessage.Text = text;
        SettingsMessage.Foreground = isError ? BadBrush : OkBrush;
    }

    private void ShowStatus(StatusSnapshot snapshot, IReadOnlyList<LogRow> rows, int intervalSeconds)
    {
        if (!snapshot.Checked)
        {
            ConnectedText.Text = "尚未检查";
            ConnectedText.Foreground = IdleBrush;
            StatusBar.Background = IdleBrush;
            StatusSubText.Text = "正在获取状态…";
            SetBadge("待检查", IdleBrush, IdleBadgeBrush);
        }
        else if (snapshot.Connected)
        {
            ConnectedText.Text = "已连接";
            ConnectedText.Foreground = OkBrush;
            StatusBar.Background = OkBrush;
            StatusSubText.Text = $"上次检查 {LastCheckText(rows)} · 每 {intervalSeconds} 秒检查一次";
            SetBadge("内网可达", OkBrush, OkBadgeBrush);
        }
        else if (snapshot.AuthBlocked)
        {
            ConnectedText.Text = "未连接";
            ConnectedText.Foreground = WarnBrush;
            StatusBar.Background = WarnBrush;
            StatusSubText.Text = $"上次检查 {LastCheckText(rows)} · 认证失败，已暂停自动重拨";
            SetBadge("需更新密码", WarnBrush, WarnBadgeBrush);
        }
        else
        {
            ConnectedText.Text = "未连接";
            ConnectedText.Foreground = BadBrush;
            StatusBar.Background = BadBrush;
            StatusSubText.Text = $"上次检查 {LastCheckText(rows)} · 每 {intervalSeconds} 秒检查一次";
            SetBadge("内网不可达", BadBrush, BadBadgeBrush);
        }

        Ipv4Text.Text = string.IsNullOrWhiteSpace(snapshot.AdapterIPv4) ? "无" : snapshot.AdapterIPv4;
        TargetText.Text = snapshot.TargetExplanation;
        AuthText.Text = snapshot.AuthBlocked
            ? "密码认证失败，已停止自动重拨；请在「设置」里保存新密码"
            : "正常";

        var latest = rows.Count == 0 ? null : rows[^1];
        if (latest is null)
        {
            RecentTimeText.Text = "";
            RecentStepText.Text = "尚未检查";
            RecentEvidenceText.Text = "";
            return;
        }

        RecentTimeText.Text = latest.Time.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        RecentStepText.Text = $"{latest.Step} · {latest.Result}";
        RecentStepText.Foreground = BrushForResult(latest.Result);
        RecentEvidenceText.Text = latest.Evidence;
    }

    private void SetBadge(string text, Brush foreground, Brush background)
    {
        StatusBadgeText.Text = text;
        StatusBadgeText.Foreground = foreground;
        StatusBadge.Background = background;
    }

    private static string LastCheckText(IReadOnlyList<LogRow> rows)
    {
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (rows[i].Step == "检查")
            {
                return rows[i].Time.LocalDateTime.ToString("HH:mm:ss");
            }
        }

        return "—";
    }

    private void ShowLog()
    {
        var onlyProblems = OnlyProblemsCheck.IsChecked == true;
        var items = _logRows
            .OrderByDescending(row => row.Time)
            .Where(row => !onlyProblems || IsProblem(row.Result))
            .Select(row => new LogItem(
                row.Time.LocalDateTime.ToString("MM-dd HH:mm:ss"),
                row.Step,
                row.Result,
                row.Evidence,
                BrushForResult(row.Result)))
            .ToList();
        LogGrid.ItemsSource = items;
        var scope = _logRows.Count >= LogPageSize ? $"最近 {_logRows.Count} 条" : $"共 {_logRows.Count} 条";
        LogCountText.Text = onlyProblems
            ? $"{scope}，其中异常 {items.Count} 条"
            : scope;
    }

    private static bool IsProblem(string result)
    {
        return result.Contains("失败", StringComparison.Ordinal)
            || result.Contains("未连接", StringComparison.Ordinal)
            || result.Contains("未提交", StringComparison.Ordinal)
            || result.Contains("挂断", StringComparison.Ordinal)
            || result.Contains("错误", StringComparison.Ordinal);
    }

    private static Brush BrushForResult(string result)
    {
        if (result.Contains("已连接", StringComparison.Ordinal)
            || result.Contains("成功", StringComparison.Ordinal)
            || result.Contains("已重启", StringComparison.Ordinal))
        {
            return OkBrush;
        }

        if (IsProblem(result))
        {
            return BadBrush;
        }

        return Freeze(Color.FromRgb(0x4B, 0x55, 0x63));
    }
}

public sealed class AdapterRow
{
    public AdapterRow(string name, string? ipv4)
    {
        Name = name;
        IPv4 = ipv4;
    }

    public string Name { get; }

    public string? IPv4 { get; }

    public string Display => string.IsNullOrWhiteSpace(IPv4) ? Name : $"{Name}    {IPv4}";
}

public sealed record LogItem(string Time, string Step, string Result, string Evidence, Brush ResultBrush);
