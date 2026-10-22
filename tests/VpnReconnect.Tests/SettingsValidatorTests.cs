using VpnReconnect.Settings;

namespace VpnReconnect.Tests;

public class SettingsValidatorTests
{
    [Fact]
    public void 间隔14秒被拒绝()
    {
        var error = SettingsValidator.Validate(new AppSettings("公司", null, null, "bjj", 14, true));
        Assert.NotNull(error);
    }

    [Fact]
    public void 间隔15和600秒通过()
    {
        Assert.Null(SettingsValidator.Validate(new AppSettings("公司", "192.168.1.21", "公司", "bjj", 15, true)));
        Assert.Null(SettingsValidator.Validate(new AppSettings("公司", null, null, "bjj", 600, false)));
    }

    [Fact]
    public void 空连接名被拒绝()
    {
        Assert.NotNull(SettingsValidator.Validate(new AppSettings("  ", null, null, "bjj", 60, true)));
    }
}
