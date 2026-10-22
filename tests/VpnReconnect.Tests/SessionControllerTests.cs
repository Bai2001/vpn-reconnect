using VpnReconnect.Session;
using VpnReconnect.Startup;

namespace VpnReconnect.Tests;

public class SessionControllerTests
{
    [Fact]
    public void 关闭窗口不停止检查_退出才停止()
    {
        var session = new SessionController();
        session.StartBackground();
        session.ShowWindow();
        session.CloseWindow();
        Assert.False(session.WindowVisible);
        Assert.True(session.CheckerRunning);
        session.Exit();
        Assert.False(session.CheckerRunning);
        Assert.False(session.WindowVisible);
    }

    [Fact]
    public void 开机任务是登录时最高权限后台启动()
    {
        var spec = LoginTaskSpec.For(@"C:\app\VpnReconnect.exe");
        Assert.Equal(@"C:\app\VpnReconnect.exe", spec.ExePath);
        Assert.Equal("--background", spec.Arguments);
        Assert.True(spec.HighestPrivileges);
        Assert.Equal("OnLogon", spec.Trigger);
    }
}
