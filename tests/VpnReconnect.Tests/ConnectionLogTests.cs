using VpnReconnect.Logging;

namespace VpnReconnect.Tests;

public class ConnectionLogTests
{
    [Fact]
    public void 依据里的密码会被替换()
    {
        var path = Path.GetTempFileName();
        var exported = Path.GetTempFileName();
        try
        {
            var log = new ConnectionLog(path, () => DateTimeOffset.Parse("2026-10-22T09:00:14+08:00"));
            log.Append("拨号", "失败", "密码 secret-value 无效", "secret-value");
            Assert.DoesNotContain("secret-value", File.ReadAllText(path));
            log.ExportTo(exported);
            Assert.DoesNotContain("secret-value", File.ReadAllText(exported));
        }
        finally
        {
            File.Delete(exported);
            File.Delete(path);
        }
    }

    [Fact]
    public void 清空后读不到旧记录()
    {
        var path = Path.GetTempFileName();
        try
        {
            var log = new ConnectionLog(path, () => DateTimeOffset.UnixEpoch);
            log.Append("检查", "未连接", "适配器无有效 IPv4", null);
            log.Clear();
            Assert.Empty(log.ReadAll());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 超出天数或体积时删除最旧行()
    {
        var path = Path.GetTempFileName();
        try
        {
            var now = DateTimeOffset.Parse("2026-10-22T09:00:14+08:00");
            var clock = now;
            var log = new ConnectionLog(path, () => clock);

            clock = now - TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1);
            log.Append("检查", "未连接", "太旧", null);
            clock = now;
            log.Append("检查", "未连接", "旧行" + new string('a', 80), null);
            var afterFirstRecent = new FileInfo(path).Length;
            log.Append("检查", "未连接", "新行" + new string('b', 80), null);

            log.Rotate(afterFirstRecent, TimeSpan.FromDays(7));

            var rows = log.ReadAll();
            Assert.DoesNotContain(rows, row => row.Evidence == "太旧");
            Assert.Single(rows);
            Assert.Equal("新行" + new string('b', 80), rows[0].Evidence);
            Assert.True(new FileInfo(path).Length <= afterFirstRecent);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
