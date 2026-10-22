using System.IO.Compression;
using System.Text;
using VpnReconnect.Settings;
using VpnReconnect.Updates;

namespace VpnReconnect.Tests;

public class UpdateTests : IDisposable
{
    private readonly List<string> _paths = [];

    [Theory]
    [InlineData("1.0.2", 1, 0, 2, false)]
    [InlineData("v1.0.2", 1, 0, 2, false)]
    [InlineData("V1.2", 1, 2, 0, false)]
    [InlineData("1.0.2-beta", 1, 0, 2, true)]
    public void 能解析各种版本号写法(string text, int major, int minor, int patch, bool prerelease)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(new ReleaseVersion(major, minor, patch, prerelease), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("1.2.3.4.5")]
    public void 非法版本号解析失败(string text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out _));
    }

    [Fact]
    public void 版本比较按数字顺序且预发布低于正式版()
    {
        Assert.True(Parse("1.0.2").CompareTo(Parse("1.0.1")) > 0);
        Assert.True(Parse("1.1.0").CompareTo(Parse("1.0.9")) > 0);
        Assert.True(Parse("1.0.2-beta").CompareTo(Parse("1.0.2")) < 0);
        Assert.Equal(0, Parse("1.0.2").CompareTo(Parse("v1.0.2")));
    }

    [Fact]
    public void 只有更高版本才提示更新()
    {
        var release = Release("v1.0.2");
        Assert.True(UpdateEvaluator.IsNewer(Parse("1.0.1"), release));
        Assert.False(UpdateEvaluator.IsNewer(Parse("1.0.2"), release));
        Assert.False(UpdateEvaluator.IsNewer(Parse("1.0.3"), release));
    }

    [Fact]
    public void 缺少安装包或校验和就不能自动安装()
    {
        Assert.True(UpdateEvaluator.CanInstall(Release("v1.0.2")));
        Assert.False(UpdateEvaluator.CanInstall(Release("v1.0.2") with { Package = null }));
        Assert.False(UpdateEvaluator.CanInstall(Release("v1.0.2") with { PackageChecksum = null }));
    }

    [Theory]
    [InlineData("VpnReconnect-1.0.2-win-x64.zip", "1.0.2")]
    [InlineData("VpnReconnect-2.3-win-x64.zip", "2.3.0")]
    [InlineData("VpnReconnect-1.0.2-win-x64.zip.sha256", "1.0.2")]
    public void 能从资产名解析版本(string name, string expected)
    {
        var version = UpdateEvaluator.VersionFromAssetName(name);
        Assert.NotNull(version);
        Assert.Equal(expected, version.Value.ToString());
    }

    [Fact]
    public void 非本程序的资产解析不出版本()
    {
        Assert.Null(UpdateEvaluator.VersionFromAssetName("README.md"));
        Assert.Null(UpdateEvaluator.VersionFromAssetName("VpnReconnect.zip"));
    }

    [Fact]
    public void 校验和文件能解析并校验文件()
    {
        var path = NewFile("hash-me.bin");
        File.WriteAllText(path, "hello");
        var hash = HashVerifier.ComputeFileHash(path);

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, HashVerifier.ParseHash($"{hash}  VpnReconnect-1.0.2-win-x64.zip\n"));
        Assert.True(HashVerifier.Matches(path, hash));
        Assert.False(HashVerifier.Matches(path, new string('0', 64)));
        Assert.Null(HashVerifier.ParseHash("not-a-hash"));
        Assert.Null(HashVerifier.ParseHash(""));
    }

    [Fact]
    public void 替换运行时保留旧文件作为备份()
    {
        var dir = NewDirectory();
        var current = Path.Combine(dir, "VpnReconnect.exe");
        var fresh = Path.Combine(dir, "VpnReconnect.exe.new");
        File.WriteAllText(current, "旧版本");
        File.WriteAllText(fresh, "新版本");

        UpdateSwapper.Replace(current, fresh);

        Assert.Equal("新版本", File.ReadAllText(current));
        Assert.Equal("旧版本", File.ReadAllText(UpdateSwapper.BackupPathFor(current)));
    }

    [Fact]
    public void 新文件不存在时不破坏当前程序()
    {
        var dir = NewDirectory();
        var current = Path.Combine(dir, "VpnReconnect.exe");
        File.WriteAllText(current, "旧版本");

        Assert.Throws<FileNotFoundException>(() => UpdateSwapper.Replace(current, Path.Combine(dir, "missing.exe")));
        Assert.Equal("旧版本", File.ReadAllText(current));
    }

    [Fact]
    public void 清理备份会删掉旧文件()
    {
        var dir = NewDirectory();
        var current = Path.Combine(dir, "VpnReconnect.exe");
        var backup = UpdateSwapper.BackupPathFor(current);
        File.WriteAllText(current, "新版本");
        File.WriteAllText(backup, "旧版本");

        Assert.True(UpdateSwapper.TryCleanupBackup(current));
        Assert.False(File.Exists(backup));
        Assert.True(File.Exists(current));
    }

    [Fact]
    public void 能从zip里取出exe()
    {
        var dir = NewDirectory();
        var zipPath = Path.Combine(dir, "package.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var stream = zip.CreateEntry(UpdateSwapper.ExeEntryName).Open())
            {
                stream.Write(Encoding.UTF8.GetBytes("新版本内容"));
            }

            using var readmeStream = zip.CreateEntry("README.md").Open();
            readmeStream.Write(Encoding.UTF8.GetBytes("# 说明"));
        }

        var extracted = UpdateSwapper.ExtractExe(zipPath, Path.Combine(dir, "out", "VpnReconnect.exe"));
        Assert.Equal("新版本内容", File.ReadAllText(extracted));
    }

    [Fact]
    public void 没有exe的zip会报错()
    {
        var dir = NewDirectory();
        var zipPath = Path.Combine(dir, "package.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("README.md");
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes("# 说明"));
        }

        Assert.Throws<InvalidDataException>(() => UpdateSwapper.ExtractExe(zipPath, Path.Combine(dir, "VpnReconnect.exe")));
    }

    [Fact]
    public void 旧配置没有更新开关时默认为开启()
    {
        const string legacy = """
        {
          "ConnectionName": "公司",
          "TargetAddress": "192.168.4.100",
          "SelectedAdapterName": "公司",
          "Username": "bjj",
          "IntervalSeconds": 30,
          "StartOnLogin": true
        }
        """;

        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(legacy);
        Assert.NotNull(settings);
        Assert.True(settings.AutoCheckUpdates);
        Assert.Null(settings.CheckUpdates);
    }

    [Fact]
    public void 关闭后保存再读取仍是关闭()
    {
        var settings = new AppSettings("公司", null, "公司", "bjj", 30, false, CheckUpdates: false);
        var json = System.Text.Json.JsonSerializer.Serialize(settings);

        Assert.DoesNotContain("AutoCheckUpdates", json);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(back);
        Assert.False(back.AutoCheckUpdates);
    }

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static ReleaseVersion Parse(string text)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        return version;
    }

    private static ReleaseInfo Release(string tag)
    {
        var version = Parse(tag);
        var name = $"VpnReconnect-{version}-win-x64.zip";
        return new ReleaseInfo(
            tag,
            version,
            "备注",
            "https://github.com/Bai2001/vpn-reconnect/releases/tag/" + tag,
            new ReleaseAsset(name, $"https://example.test/{name}", 100),
            new ReleaseAsset(name + ".sha256", $"https://example.test/{name}.sha256", 90));
    }

    private string NewFile(string name)
    {
        var path = Path.Combine(NewDirectory(), name);
        _paths.Add(path);
        return path;
    }

    private string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vpn-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _paths.Add(path);
        return path;
    }
}
