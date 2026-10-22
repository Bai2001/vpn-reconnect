namespace VpnReconnect.Settings;

public sealed record AppSettings(
    string ConnectionName,
    string? TargetAddress,
    string? SelectedAdapterName,
    string Username,
    int IntervalSeconds,
    bool StartOnLogin);
