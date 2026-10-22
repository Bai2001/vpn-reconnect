using VpnReconnect.Health;
using VpnReconnect.Logging;
using VpnReconnect.Recovery;
using VpnReconnect.Settings;

namespace VpnReconnect.Session;

public interface IAdapterLookup
{
    AdapterSnapshot? Get(string selectedAdapterName);
}

public sealed class CheckService
{
    private readonly AppSettings _settings;
    private readonly IAdapterLookup _lookup;
    private readonly IPing _ping;
    private readonly IRecoveryIo _io;
    private readonly RecoveryCycle _cycle;
    private readonly ConnectionLog _log;
    private readonly CheckLogThrottle _throttle;
    private readonly Func<(string Username, string Password)?> _loadCredentials;

    public CheckService(
        AppSettings settings,
        IAdapterLookup lookup,
        IPing ping,
        IRecoveryIo io,
        RecoveryCycle cycle,
        ConnectionLog log,
        Func<(string Username, string Password)?> loadCredentials,
        CheckLogThrottle? throttle = null)
    {
        _settings = settings;
        _lookup = lookup;
        _ping = ping;
        _io = io;
        _cycle = cycle;
        _log = log;
        _throttle = throttle ?? new CheckLogThrottle();
        _loadCredentials = loadCredentials;
    }

    public HealthVerdict CheckOnce()
    {
        var adapter = string.IsNullOrWhiteSpace(_settings.SelectedAdapterName)
            ? null
            : _lookup.Get(_settings.SelectedAdapterName);
        var verdict = ConnectivityJudge.Judge(
            _settings.TargetAddress,
            _settings.SelectedAdapterName,
            adapter,
            _ping);

        if (verdict.Connected)
        {
            if (_throttle.ShouldWrite("已连接"))
            {
                _log.Append("检查", "已连接", verdict.Evidence, null);
            }

            return verdict;
        }

        if (_throttle.ShouldWrite("未连接"))
        {
            _log.Append("检查", "未连接", verdict.Evidence, null);
        }

        var credentials = _loadCredentials();
        if (credentials is not { } saved
            || string.IsNullOrWhiteSpace(saved.Username)
            || string.IsNullOrWhiteSpace(saved.Password))
        {
            _log.Append("拨号", "未提交用户名", "没有保存的用户名或密码，不拨号", null);
            return verdict;
        }

        foreach (var item in _cycle.RunOnce(_io, _settings.ConnectionName, saved.Username, saved.Password))
        {
            _log.Append(item.Step, item.Result, item.Evidence, saved.Password);
        }

        return verdict;
    }

    public void SavePassword(string username, string password)
    {
        _ = username;
        _ = password;
        _cycle.NotePasswordSaved();
        _throttle.Reset();
    }
}
