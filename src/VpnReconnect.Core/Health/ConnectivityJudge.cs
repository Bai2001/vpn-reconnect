namespace VpnReconnect.Health;

public sealed record HealthVerdict(bool Connected, string Evidence, string? AdapterIPv4);

public static class ConnectivityJudge
{
    public static HealthVerdict Judge(
        string? targetAddress,
        string? selectedAdapterName,
        AdapterSnapshot? selectedAdapter,
        IPing ping)
    {
        var adapter = MatchSelected(selectedAdapterName, selectedAdapter);
        var adapterIpv4 = Normalize(adapter?.IPv4);
        var target = Normalize(targetAddress);

        if (target is not null)
        {
            var reachable = ping.Ping(target, 2, TimeSpan.FromSeconds(1));
            var adapterText = adapter is null
                ? "未选择适配器"
                : $"所选接口 {adapter.ConnectionName} 地址 {adapterIpv4 ?? "无"}";
            var evidence = reachable
                ? $"目标 {target} ping 通，{adapterText}"
                : $"目标 {target} ping 失败，{adapterText}";
            return new HealthVerdict(reachable, evidence, adapterIpv4);
        }

        if (adapter is null)
        {
            return new HealthVerdict(false, "未选择适配器", null);
        }

        if (!HasUsableIpv4(adapterIpv4))
        {
            return new HealthVerdict(false, $"所选接口 {adapter.ConnectionName} 适配器无有效 IPv4", adapterIpv4);
        }

        return new HealthVerdict(true, $"所选接口 {adapter.ConnectionName} 已获得 IPv4 {adapterIpv4}", adapterIpv4);
    }

    private static AdapterSnapshot? MatchSelected(string? selectedAdapterName, AdapterSnapshot? selectedAdapter)
    {
        var name = Normalize(selectedAdapterName);
        if (name is null || selectedAdapter is null || selectedAdapter.ConnectionName != name)
        {
            return null;
        }

        return selectedAdapter;
    }

    private static bool HasUsableIpv4(string? ipv4)
    {
        return ipv4 is not null && ipv4 != "0.0.0.0" && !ipv4.StartsWith("169.254.", StringComparison.Ordinal);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
