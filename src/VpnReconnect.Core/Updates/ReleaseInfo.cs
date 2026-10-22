namespace VpnReconnect.Updates;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

/// <summary>一个 GitHub Release 里跟本程序相关的部分。</summary>
public sealed record ReleaseInfo(
    string Tag,
    ReleaseVersion Version,
    string Notes,
    string PageUrl,
    ReleaseAsset? Package,
    ReleaseAsset? PackageChecksum);

public static class UpdateEvaluator
{
    /// <summary>Release 里打包的 zip 名（含版本号，形如 VpnReconnect-1.0.2-win-x64.zip）。</summary>
    public const string PackageSuffix = "-win-x64.zip";

    public static bool IsNewer(ReleaseVersion current, ReleaseInfo release)
    {
        return release.Version.CompareTo(current) > 0;
    }

    /// <summary>没有包或没有校验和就不自动安装，只提示去网页下载。</summary>
    public static bool CanInstall(ReleaseInfo release)
    {
        return release.Package is not null && release.PackageChecksum is not null;
    }

    /// <summary>从资产名里取出版本号，用于校验资产和 tag 是否一致。</summary>
    public static ReleaseVersion? VersionFromAssetName(string name)
    {
        const string prefix = "VpnReconnect-";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = name[prefix.Length..];
        var index = rest.IndexOf("-win-x64", StringComparison.OrdinalIgnoreCase);
        if (index <= 0)
        {
            return null;
        }

        return ReleaseVersion.TryParse(rest[..index], out var version) ? version : null;
    }
}
