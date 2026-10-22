namespace VpnReconnect.Health;

public interface IPing
{
    bool Ping(string address, int attempts, TimeSpan perAttemptTimeout);
}
