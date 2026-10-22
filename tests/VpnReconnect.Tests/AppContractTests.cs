namespace VpnReconnect.Tests;

public class AppContractTests
{
    [Fact]
    public void 主窗口有状态设置日志三页且不包含公网服务器默认值()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "vpn-reconnect.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var xamlPath = Path.Combine(dir!.FullName, "src", "VpnReconnect.App", "MainWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);
        Assert.Contains("状态", xaml);
        Assert.Contains("设置", xaml);
        Assert.Contains("日志", xaml);
        Assert.DoesNotContain("101.207.248.238", xaml);
    }
}
