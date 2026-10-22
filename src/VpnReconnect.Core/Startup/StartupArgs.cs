namespace VpnReconnect.Startup;

/// <summary>启动参数：开机/计划任务启动时走 <see cref="Background"/>，不弹窗口。</summary>
public static class StartupArgs
{
    public const string Background = "--background";

    public static bool IsBackground(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, Background, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
