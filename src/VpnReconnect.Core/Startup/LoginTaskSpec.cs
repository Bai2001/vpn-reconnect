namespace VpnReconnect.Startup;

public sealed record LoginTaskSpec(string ExePath, string Arguments, bool HighestPrivileges, string Trigger)
{
    public static LoginTaskSpec For(string exePath)
    {
        return new LoginTaskSpec(exePath, "--background", true, "OnLogon");
    }
}
