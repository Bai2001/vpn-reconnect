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
}
