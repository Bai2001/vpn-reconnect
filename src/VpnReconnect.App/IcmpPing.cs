using System.Net.NetworkInformation;
using VpnReconnect.Health;

namespace VpnReconnect.App;

public sealed class IcmpPing : IPing
{
    public bool Ping(string address, int attempts, TimeSpan perAttemptTimeout)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var tries = Math.Clamp(attempts, 1, 2);
        var timeout = (int)Math.Clamp(perAttemptTimeout.TotalMilliseconds, 1, 1000);
        for (var i = 0; i < tries; i++)
        {
            try
            {
                using var ping = new Ping();
                var reply = ping.Send(address, timeout);
                if (reply?.Status == IPStatus.Success)
                {
                    return true;
                }
            }
            catch (PingException)
            {
            }
            catch (ArgumentException)
            {
            }
        }

        return false;
    }
}
