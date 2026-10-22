using VpnReconnect.Logging;

namespace VpnReconnect.Tests;

public class LogVolumeTests : IDisposable
{
    private readonly List<string> _files = [];

    [Fact]
    public void 只读尾部N条()
    {
        var log = NewLog(out _);
        for (var i = 0; i < 50; i++)
        {
            log.Append("检查", "已连接", $"第{i}条", null);
        }

        var tail = log.ReadTail(10);
        Assert.Equal(10, tail.Count);
        Assert.Equal("第40条", tail[0].Evidence);
        Assert.Equal("第49条", tail[^1].Evidence);
    }

    [Fact]
    public void 日志不足N条时全部返回()
    {
        var log = NewLog(out _);
        log.Append("检查", "已连接", "a", null);
        log.Append("检查", "未连接", "b", null);

        var tail = log.ReadTail(10);
        Assert.Equal(2, tail.Count);
        Assert.Equal("a", tail[0].Evidence);
        Assert.Equal("b", tail[1].Evidence);
    }

    [Fact]
    public void 尾部读取能跨多个读块()
    {
        var log = NewLog(out _);
        var filler = new string('x', 900);
        for (var i = 0; i < 200; i++)
        {
            log.Append("检查", "已连接", $"{i:000}-{filler}", null);
        }

        var tail = log.ReadTail(5);
        Assert.Equal(5, tail.Count);
        Assert.StartsWith("195-", tail[0].Evidence);
        Assert.StartsWith("199-", tail[^1].Evidence);
    }

    [Fact]
    public void 体积和天数都没超时不重写文件()
    {
        var path = NewPath();
        var log = new ConnectionLog(path, () => DateTimeOffset.Parse("2026-10-22T09:00:00+08:00"));
        log.Append("检查", "已连接", "一", null);
        log.Append("检查", "已连接", "二", null);
        var before = File.ReadAllText(path);
        var stamp = new FileInfo(path).LastWriteTimeUtc;

        log.Rotate(5 * 1024 * 1024, TimeSpan.FromDays(7));

        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(stamp, new FileInfo(path).LastWriteTimeUtc);
    }

    [Fact]
    public void 状态不变时按心跳间隔才记录()
    {
        var now = DateTimeOffset.Parse("2026-10-22T09:00:00+08:00");
        var throttle = new CheckLogThrottle(TimeSpan.FromMinutes(5), () => now);

        Assert.True(throttle.ShouldWrite("已连接"));
        Assert.False(throttle.ShouldWrite("已连接"));

        now = now.AddMinutes(4);
        Assert.False(throttle.ShouldWrite("已连接"));

        now = now.AddMinutes(2);
        Assert.True(throttle.ShouldWrite("已连接"));
    }

    [Fact]
    public void 状态变化立即记录()
    {
        var now = DateTimeOffset.Parse("2026-10-22T09:00:00+08:00");
        var throttle = new CheckLogThrottle(TimeSpan.FromMinutes(5), () => now);

        Assert.True(throttle.ShouldWrite("已连接"));
        now = now.AddSeconds(30);
        Assert.True(throttle.ShouldWrite("未连接"));
        now = now.AddSeconds(30);
        Assert.True(throttle.ShouldWrite("已连接"));
    }

    [Fact]
    public void 一天30秒间隔的条目数下降一个数量级()
    {
        var now = DateTimeOffset.Parse("2026-10-22T00:00:00+08:00");
        var throttle = new CheckLogThrottle(TimeSpan.FromMinutes(5), () => now);
        var written = 0;
        for (var i = 0; i < 2880; i++)
        {
            if (throttle.ShouldWrite("已连接"))
            {
                written++;
            }

            now = now.AddSeconds(30);
        }

        Assert.True(written <= 300, $"仍写入了 {written} 条");
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

    private ConnectionLog NewLog(out string path)
    {
        var clock = DateTimeOffset.Parse("2026-10-22T09:00:00+08:00");
        path = Path.Combine(Path.GetTempPath(), $"vpn-log-{Guid.NewGuid():N}.jsonl");
        _files.Add(path);
        return new ConnectionLog(path, () => clock);
    }

    private string NewPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vpn-log-{Guid.NewGuid():N}.jsonl");
        _files.Add(path);
        return path;
    }
}
