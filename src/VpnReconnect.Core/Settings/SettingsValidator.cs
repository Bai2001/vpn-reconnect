namespace VpnReconnect.Settings;

public static class SettingsValidator
{
    public static string? Validate(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ConnectionName))
        {
            return "连接名不能为空";
        }

        if (settings.IntervalSeconds < 15 || settings.IntervalSeconds > 600)
        {
            return "检查间隔必须在 15 到 600 秒之间";
        }

        return null;
    }

    public static AppSettings Normalize(AppSettings settings)
    {
        return settings with
        {
            ConnectionName = settings.ConnectionName.Trim(),
            TargetAddress = string.IsNullOrWhiteSpace(settings.TargetAddress) ? null : settings.TargetAddress.Trim(),
            SelectedAdapterName = string.IsNullOrWhiteSpace(settings.SelectedAdapterName) ? null : settings.SelectedAdapterName.Trim(),
            Username = settings.Username.Trim(),
        };
    }
}
