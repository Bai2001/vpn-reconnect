using VpnReconnect.Startup;

namespace VpnReconnect.Tests;

public class AutoStartPlannerTests
{
    [Theory]
    [InlineData(false, true, false, AutoStartMode.Disabled)]
    [InlineData(false, false, true, AutoStartMode.Disabled)]
    [InlineData(true, true, false, AutoStartMode.ScheduledTask)]
    [InlineData(true, true, true, AutoStartMode.ScheduledTask)]
    [InlineData(true, false, false, AutoStartMode.RunKey)]
    [InlineData(true, false, true, AutoStartMode.ScheduledTask)]
    public void 按权限与现状选择自启动方式(bool requested, bool elevated, bool taskExists, AutoStartMode expected)
    {
        Assert.Equal(expected, AutoStartPlanner.Decide(requested, elevated, taskExists));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void 调试构建不注册开机启动(bool elevated, bool taskExists, bool ignored)
    {
        _ = ignored;
        Assert.Equal(AutoStartMode.SkippedDebugBuild, AutoStartPlanner.Decide(true, elevated, taskExists, debugBuild: true));
    }

    [Fact]
    public void 不启用开机启动时调试标记不影响结果()
    {
        Assert.Equal(AutoStartMode.Disabled, AutoStartPlanner.Decide(false, false, false, debugBuild: true));
    }
}
