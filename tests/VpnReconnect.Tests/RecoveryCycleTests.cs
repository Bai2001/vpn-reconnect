using VpnReconnect.Recovery;

namespace VpnReconnect.Tests;

public class RecoveryCycleTests
{
    [Fact]
    public void 已连通时不拨号不重启()
    {
        var io = new FakeIo { Healthy = true };
        var events = new RecoveryCycle().RunOnce(io, "公司", "bjj", "pw");
        Assert.Empty(io.Dials);
        Assert.Equal(0, io.Restarts);
        Assert.Empty(io.Order);
        Assert.Contains(events, item => item.Result.Contains("已连通"));
    }

    [Fact]
    public void 第一次拨通就不再重启()
    {
        var io = new FakeIo { HealthyAfterDials = 1 };
        var events = new RecoveryCycle().RunOnce(io, "公司", "bjj", "pw");
        Assert.Single(io.Dials);
        Assert.Equal(0, io.Restarts);
        Assert.True(io.Dials[0].UsernameSubmitted);
        Assert.Contains(events, item => item.Evidence.Contains("已提交用户名"));
    }

    [Fact]
    public void 第一次628后只重启一次并再拨一次()
    {
        var io = new FakeIo { First = new DialReport(DialFinish.Failed, 628, true) };
        new RecoveryCycle().RunOnce(io, "公司", "bjj", "pw");
        Assert.Equal(2, io.Dials.Count);
        Assert.Equal(1, io.Restarts);
        Assert.Equal(1, io.L2tpRestarts);
    }

    [Fact]
    public void 错误码691后不再重启且下一轮不拨()
    {
        var io = new FakeIo { First = new DialReport(DialFinish.AuthRejected, 691, true) };
        var cycle = new RecoveryCycle();
        cycle.RunOnce(io, "公司", "bjj", "pw");
        Assert.True(cycle.AuthBlocked);
        Assert.Equal(0, io.Restarts);
        io.Dials.Clear();
        cycle.RunOnce(io, "公司", "bjj", "pw");
        Assert.Empty(io.Dials);
        cycle.NotePasswordSaved();
        io.First = new DialReport(DialFinish.Up, null, true);
        cycle.RunOnce(io, "公司", "bjj", "pw");
        Assert.Single(io.Dials);
    }

    [Fact]
    public void 挂死时先结束残留再重启()
    {
        var io = new FakeIo { First = new DialReport(DialFinish.Hung, null, true) };
        new RecoveryCycle().RunOnce(io, "公司", "bjj", "pw");
        Assert.Contains("Kill", io.Order);
        Assert.True(io.Order.IndexOf("Kill") < io.Order.IndexOf("Restart"));
        Assert.Equal(1, io.Restarts);
    }

    [Fact]
    public void 用户名或密码空白时不拨号不重启()
    {
        var io = new FakeIo();
        var missingUser = new RecoveryCycle().RunOnce(io, "公司", "  ", "pw");
        Assert.Empty(io.Dials);
        Assert.Equal(0, io.Restarts);
        Assert.Contains(missingUser, item => item.Evidence.Contains("未提交用户名"));

        var missingPassword = new RecoveryCycle().RunOnce(io, "公司", "bjj", "");
        Assert.Empty(io.Dials);
        Assert.Contains(missingPassword, item => item.Result.Contains("未提交用户名"));
    }

    [Fact]
    public void 错误码756先结束残留再重启且不循环()
    {
        var busy = new DialReport(DialFinish.Failed, 756, true);
        var io = new FakeIo { First = busy, After = busy };
        new RecoveryCycle().RunOnce(io, "公司", "bjj", "pw");
        Assert.Equal(2, io.Dials.Count);
        Assert.Equal(1, io.Restarts);
        Assert.Equal(1, io.L2tpRestarts);
        Assert.True(io.Order.IndexOf("Kill") < io.Order.IndexOf("Restart"));
    }

    [Fact]
    public void 错误码691即使失败结束态也不重启()
    {
        var io = new FakeIo { First = new DialReport(DialFinish.Failed, 691, true) };
        var cycle = new RecoveryCycle();
        cycle.RunOnce(io, "公司", "bjj", "pw");
        Assert.True(cycle.AuthBlocked);
        Assert.Equal(0, io.Restarts);
        Assert.Single(io.Dials);
    }

    [Fact]
    public void L2tp网卡重启后未回来再重启Ip小型端口()
    {
        var io = new FakeIo
        {
            First = new DialReport(DialFinish.Failed, 628, true),
            L2tpReturnsAfterRestart = false,
        };
        new RecoveryCycle().RunOnce(io, "公司", "bjj", "pw");
        Assert.Equal(1, io.IpRestarts);
        Assert.Equal(1, io.L2tpRestarts);
        Assert.True(io.Order.IndexOf("Restart") < io.Order.LastIndexOf("Restart"));
    }

    [Fact]
    public void 未提交用户名的拨号结果会写入依据()
    {
        var io = new FakeIo { First = new DialReport(DialFinish.Failed, 628, false) };
        var events = new RecoveryCycle().RunOnce(io, "公司", "bjj", "pw");
        Assert.Contains(events, item => item.Step == "拨号" && item.Evidence.Contains("未提交用户名"));
    }

    private sealed class FakeIo : IRecoveryIo
    {
        public bool Healthy { get; set; }

        public int HealthyAfterDials { get; set; }

        public DialReport? First { get; set; }

        public DialReport? After { get; set; }

        public bool Residual { get; set; }

        public bool L2tpPresent { get; set; } = true;

        public bool L2tpReturnsAfterRestart { get; set; } = true;

        public List<DialReport> Dials { get; } = new();

        public List<string> Order { get; } = new();

        public int Restarts { get; private set; }

        public int L2tpRestarts { get; private set; }

        public int IpRestarts { get; private set; }

        public bool IsHealthy()
        {
            return Healthy || (HealthyAfterDials > 0 && Dials.Count >= HealthyAfterDials);
        }

        public bool HasResidualDial()
        {
            return Residual;
        }

        public void KillResidualAndDisconnect()
        {
            Order.Add("Kill");
            Residual = false;
        }

        public DialReport Dial(string connectionName, string username, string password)
        {
            var scripted = Dials.Count == 0 ? First : After;
            var report = scripted ?? new DialReport(DialFinish.Up, null, !string.IsNullOrWhiteSpace(username));
            Dials.Add(report);
            return report;
        }

        public bool L2tpMiniportPresent()
        {
            return L2tpPresent;
        }

        public void RestartRasManAndIkeext()
        {
            Order.Add("Restart");
            Restarts++;
        }

        public void RestartL2tpMiniport()
        {
            Order.Add("Restart");
            L2tpRestarts++;
            L2tpPresent = L2tpReturnsAfterRestart;
        }

        public void RestartIpMiniports()
        {
            Order.Add("Restart");
            IpRestarts++;
        }
    }
}
