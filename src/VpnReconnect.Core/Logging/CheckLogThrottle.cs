namespace VpnReconnect.Logging;

/// <summary>
/// 「检查」结果的重复行降噪：40 秒一次检查时，一天上千条「已连接」会把真正有用的断开、
/// 拨号、重启记录淹没。状态一变立即写，状态不变时按心跳间隔（默认 5 分钟）留一条。
/// </summary>
public sealed class CheckLogThrottle
{
    private readonly TimeSpan _heartbeat;
    private readonly Func<DateTimeOffset> _clock;
    private string? _lastResult;
    private DateTimeOffset _lastWrite;

    public CheckLogThrottle(TimeSpan? heartbeat = null, Func<DateTimeOffset>? clock = null)
    {
        _heartbeat = heartbeat ?? TimeSpan.FromMinutes(5);
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public bool ShouldWrite(string result)
    {
        var now = _clock();
        if (_lastResult is null || !string.Equals(_lastResult, result, StringComparison.Ordinal))
        {
            _lastResult = result;
            _lastWrite = now;
            return true;
        }

        if (now - _lastWrite >= _heartbeat)
        {
            _lastWrite = now;
            return true;
        }

        return false;
    }

    /// <summary>忘记上一次结果，下次检查一定写一条（比如用户刚保存了新密码）。</summary>
    public void Reset()
    {
        _lastResult = null;
    }
}
