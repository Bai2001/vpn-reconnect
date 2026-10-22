namespace VpnReconnect.Startup;

/// <summary>开机自启动的落地方式。</summary>
public enum AutoStartMode
{
    /// <summary>不开机启动。</summary>
    Disabled = 0,

    /// <summary>计划任务（最高权限，断线时才能重启服务）。</summary>
    ScheduledTask = 1,

    /// <summary>注册表 HKCU Run（不需要管理员，但只有普通权限）。</summary>
    RunKey = 2,

    /// <summary>调试构建不注册开机启动，避免把 bin\Debug 的 exe 设成自启动。</summary>
    SkippedDebugBuild = 3,
}

/// <summary>
/// 决定用哪种自启动方式：创建计划任务需要管理员（否则 schtasks 报拒绝访问），
/// 非提权时退回 HKCU Run 键；已有计划任务时保持不动，避免同一次登录启动两份。
/// </summary>
public static class AutoStartPlanner
{
    public static AutoStartMode Decide(bool requested, bool elevated, bool taskExists, bool debugBuild = false)
    {
        if (!requested)
        {
            return AutoStartMode.Disabled;
        }

        if (debugBuild)
        {
            return AutoStartMode.SkippedDebugBuild;
        }

        if (taskExists)
        {
            return AutoStartMode.ScheduledTask;
        }

        return elevated ? AutoStartMode.ScheduledTask : AutoStartMode.RunKey;
    }
}
