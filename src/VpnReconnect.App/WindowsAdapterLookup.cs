using System.Net.NetworkInformation;
using System.Net.Sockets;
using VpnReconnect.Health;
using VpnReconnect.Session;

namespace VpnReconnect.App;

public sealed class WindowsAdapterLookup : IAdapterLookup
{
    public AdapterSnapshot? Get(string selectedAdapterName)
    {
        if (string.IsNullOrWhiteSpace(selectedAdapterName))
        {
            return null;
        }

        var name = selectedAdapterName.Trim();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!string.Equals(nic.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            return new AdapterSnapshot(nic.Name, ReadIpv4(nic));
        }

        return null;
    }

    public IReadOnlyList<AdapterSnapshot> ListWithIpv4()
    {
        var list = new List<AdapterSnapshot>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var ipv4 = ReadIpv4(nic);
            if (ipv4 is null || ipv4 == "0.0.0.0")
            {
                continue;
            }

            list.Add(new AdapterSnapshot(nic.Name, ipv4));
        }

        return list;
    }

    private static string? ReadIpv4(NetworkInterface nic)
    {
        string? fallback = null;
        foreach (var address in nic.GetIPProperties().UnicastAddresses)
        {
            if (address.Address.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            var text = address.Address.ToString();
            if (text == "0.0.0.0")
            {
                fallback ??= text;
                continue;
            }

            if (!text.StartsWith("169.254.", StringComparison.Ordinal))
            {
                return text;
            }

            fallback ??= text;
        }

        return fallback;
    }
}
