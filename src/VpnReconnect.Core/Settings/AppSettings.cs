using System.Text.Json.Serialization;

namespace VpnReconnect.Settings;

public sealed record AppSettings(
    string ConnectionName,
    string? TargetAddress,
    string? SelectedAdapterName,
    string Username,
    int IntervalSeconds,
    bool StartOnLogin,
    bool? CheckUpdates = null)
{
    /// <summary>旧配置文件没有这个字段时按开启处理。</summary>
    [JsonIgnore]
    public bool AutoCheckUpdates => CheckUpdates ?? true;
}
