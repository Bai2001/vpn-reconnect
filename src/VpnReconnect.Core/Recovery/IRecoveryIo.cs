namespace VpnReconnect.Recovery;

public enum DialFinish
{
    Up,
    AuthRejected,
    Failed,
    Hung,
}

public sealed record DialReport(DialFinish Finish, int? ErrorCode, bool UsernameSubmitted);

public interface IRecoveryIo
{
    bool IsHealthy();

    bool HasResidualDial();

    void KillResidualAndDisconnect();

    DialReport Dial(string connectionName, string username, string password);

    bool L2tpMiniportPresent();

    void RestartRasManAndIkeext();

    void RestartL2tpMiniport();

    void RestartIpMiniports();
}
