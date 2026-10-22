using System.Globalization;

namespace VpnReconnect.Updates;

/// <summary>形如 1.2.3 的版本号，支持 v 前缀与 -beta 这类预发布后缀。</summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, bool IsPrerelease)
    : IComparable<ReleaseVersion>
{
    public static ReleaseVersion FromSystem(Version version)
    {
        return new ReleaseVersion(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build), false);
    }

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var dash = trimmed.IndexOf('-');
        var isPrerelease = dash > 0;
        var core = dash > 0 ? trimmed[..dash] : trimmed;
        var parts = core.Split('.');
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i])
                || numbers[i] < 0)
            {
                return false;
            }
        }

        version = new ReleaseVersion(numbers[0], numbers[1], numbers[2], isPrerelease);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        if (IsPrerelease == other.IsPrerelease)
        {
            return 0;
        }

        // 预发布版本低于同号正式版
        return IsPrerelease ? -1 : 1;
    }

    public override string ToString()
    {
        return $"{Major}.{Minor}.{Patch}";
    }
}
