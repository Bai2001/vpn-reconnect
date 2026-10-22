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

    public static bool Exists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }
}
