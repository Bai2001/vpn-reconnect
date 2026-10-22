using Microsoft.Win32;

namespace VpnReconnect.App;

/// <summary>
/// 注册表 HKCU Run 自启动：不需要管理员权限，作为无法创建计划任务时的退路。
/// 代价是登录后以普通权限运行，断线时重启服务会被拒绝（拨号不受影响）。
/// </summary>
public static class RunKeyRegistrar
{
    public const string ValueName = "VpnReconnect";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{exePath}\" --background", RegistryValueKind.String);
    }

    public static void Remove()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>已注册的命令行里的程序路径（取不到返回 null）。</summary>
    public static string? RegisteredPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        if (key?.GetValue(ValueName) is not string value)
        {
            return null;
        }

        var text = value.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : text[1..];
        }

        var space = text.IndexOf(' ');
        return space > 0 ? text[..space] : text;
    }

    public static bool Exists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }
}
