using VpnReconnect.Health;
using VpnReconnect.Logging;
using VpnReconnect.Recovery;
using VpnReconnect.Session;
using VpnReconnect.Settings;

namespace VpnReconnect.Tests;

public class CheckServiceTests : IDisposable
{
    private readonly List<string> _files = [];

    [Fact]
    public void 未连接时日志能区分ping失败()
    {
        var log = NewLog();
        var ping = new FakePing(succeedsFor: "none");
        var service = NewService(log, ping, target: "192.168.1.21", adapterIp: null);
        service.CheckOnce();
        Assert.Contains(log.ReadAll(), row => row.Evidence.Contains("192.168.1.21") && row.Result.Contains("未连接"));
    }

    [Fact]
    public void 已连接时不进入恢复()
    {
        var io = new FakeIo { Healthy = true };
        var service = NewService(NewLog(), new FakePing(always: true), target: null, adapterIp: "192.168.4.28", io);
        service.CheckOnce();
        Assert.Empty(io.Dials);
        Assert.Equal(0, io.Restarts);
    }

    public void Dispose()
    {
        foreach (var path in _files)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private ConnectionLog NewLog()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vpn-check-{Guid.NewGuid():N}.jsonl");
        _files.Add(path);
        return new ConnectionLog(path, () => DateTimeOffset.UnixEpoch);
    }

    private static CheckService NewService(
        ConnectionLog log,
        IPing ping,
        string? target,
        string? adapterIp,
        IRecoveryIo? io = null)
    {
        const string adapterName = "公司";
        var selected = adapterIp is null ? null : adapterName;
        var settings = new AppSettings("公司", target, selected, "bjj", 60, false);
        return new CheckService(
            settings,
            new FakeLookup(selected, adapterIp),
            ping,
            io ?? new FakeIo(),
            new RecoveryCycle(),
            log,
            () => ("bjj", "pw"));
    }

    private sealed class FakeLookup(string? name, string? ipv4) : IAdapterLookup
    {
        public AdapterSnapshot? Get(string selectedAdapterName)
        {
            if (name is null || selectedAdapterName != name)
            {
                return null;
            }

            return new AdapterSnapshot(name, ipv4);
        }
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

        public bool Ping(string address, int attempts, TimeSpan perAttemptTimeout)
        {
            return _succeedsFor is not null ? address == _succeedsFor : _always;
        }
    }

    private sealed class FakeIo : IRecoveryIo
    {
        public bool Healthy { get; set; }

        public List<DialReport> Dials { get; } = [];

        public int Restarts { get; private set; }

        public bool IsHealthy() => Healthy;

        public bool HasResidualDial() => false;

        public void KillResidualAndDisconnect()
        {
        }

        public DialReport Dial(string connectionName, string username, string password)
        {
            var report = new DialReport(DialFinish.Failed, 628, !string.IsNullOrWhiteSpace(username));
            Dials.Add(report);
            return report;
        }

        public bool L2tpMiniportPresent() => true;

        public void RestartRasManAndIkeext() => Restarts++;

        public void RestartL2tpMiniport()
        {
        }

        public void RestartIpMiniports()
        {
        }
    }
}
