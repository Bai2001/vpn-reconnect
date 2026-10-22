namespace VpnReconnect.Startup;

/// <summary>启动参数：开机/计划任务启动时走 <see cref="Background"/>，不弹窗口。</summary>
public static class StartupArgs
{
    public const string Background = "--background";

    /// <summary>更新后重启用：等旧进程退出并接过单实例锁，然后正常显示窗口。</summary>
    public const string Takeover = "--takeover";

    public static bool IsTakeover(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, Takeover, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

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
