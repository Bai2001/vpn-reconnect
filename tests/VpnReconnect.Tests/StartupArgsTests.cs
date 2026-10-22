using VpnReconnect.Startup;

namespace VpnReconnect.Tests;

public class StartupArgsTests
{
    [Fact]
    public void 带后台参数时识别为静默启动()
    {
        Assert.True(StartupArgs.IsBackground(["--background"]));
        Assert.True(StartupArgs.IsBackground(["--BACKGROUND"]));
        Assert.True(StartupArgs.IsBackground(["--other", "--background"]));
    }

    [Fact]
    public void 没有后台参数时正常显示窗口()
    {
        Assert.False(StartupArgs.IsBackground([]));
        Assert.False(StartupArgs.IsBackground(["--verbose"]));
    }
}
