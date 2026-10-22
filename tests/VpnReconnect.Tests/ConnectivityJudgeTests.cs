using VpnReconnect.Health;

namespace VpnReconnect.Tests;

public class ConnectivityJudgeTests
{
    [Fact]
    public void 未设目标且没有公司适配器地址时未连接()
    {
        var ping = new FakePing(always: true);
        var verdict = ConnectivityJudge.Judge(null, null, null, ping);
        Assert.False(verdict.Connected);
        Assert.Equal(0, ping.Calls);
        Assert.Equal("未选择适配器", verdict.Evidence);
    }

    [Fact]
    public void 没选适配器时即使别处有地址也不连接()
    {
        var ping = new FakePing(always: true);
        var verdict = ConnectivityJudge.Judge(null, null, new AdapterSnapshot("Mihomo", "198.18.0.1"), ping);
        Assert.False(verdict.Connected);
        Assert.Equal(0, ping.Calls);
    }

    [Fact]
    public void 零地址和169地址不算拿到IP()
    {
        var ping = new FakePing(always: true);
        Assert.False(ConnectivityJudge.Judge(null, "公司", new AdapterSnapshot("公司", "0.0.0.0"), ping).Connected);
        Assert.False(ConnectivityJudge.Judge(null, "公司", new AdapterSnapshot("公司", "169.254.1.1"), ping).Connected);
        Assert.Equal(0, ping.Calls);
    }

    [Fact]
    public void 未设目标时只认公司适配器的有效IPv4()
    {
        var ping = new FakePing(always: true);
        var verdict = ConnectivityJudge.Judge(null, "公司", new AdapterSnapshot("公司", "192.168.4.28"), ping);
        Assert.True(verdict.Connected);
        Assert.Equal("192.168.4.28", verdict.AdapterIPv4);
        Assert.Equal(0, ping.Calls);
    }

    [Fact]
    public void 设了目标时只ping该地址()
    {
        var ping = new FakePing(succeedsFor: "192.168.1.21");
        var up = ConnectivityJudge.Judge("192.168.1.21", "公司", new AdapterSnapshot("公司", null), ping);
        var down = ConnectivityJudge.Judge("192.168.1.22", "公司", new AdapterSnapshot("公司", "192.168.4.28"), ping);
        Assert.True(up.Connected);
        Assert.False(down.Connected);
        Assert.Equal("192.168.4.28", down.AdapterIPv4);
        Assert.DoesNotContain(ping.Asked, a => a == "101.207.248.238");
        Assert.Equal(["192.168.1.21", "192.168.1.22"], ping.Asked);
    }

    [Fact]
    public void 快照名字不同时当成没选适配器()
    {
        var ping = new FakePing(always: true);
        var verdict = ConnectivityJudge.Judge(null, "公司", new AdapterSnapshot("Mihomo", "198.18.0.1"), ping);
        Assert.False(verdict.Connected);
        Assert.Equal(0, ping.Calls);
        Assert.Null(verdict.AdapterIPv4);
    }

    private sealed class FakePing : IPing
    {
        private readonly bool _always;
        private readonly string? _succeedsFor;

        public FakePing(bool always = false, string? succeedsFor = null)
        {
            _always = always;
            _succeedsFor = succeedsFor;
        }

        public int Calls { get; private set; }

        public List<string> Asked { get; } = [];

        public bool Ping(string address, int attempts, TimeSpan perAttemptTimeout)
        {
            if (attempts != 2 || perAttemptTimeout != TimeSpan.FromSeconds(1))
            {
                throw new InvalidOperationException($"期望 attempts=2、超时 1 秒，实际 attempts={attempts}、超时={perAttemptTimeout}");
            }

            Calls++;
            Asked.Add(address);
            return _succeedsFor is not null ? address == _succeedsFor : _always;
        }
    }
}
