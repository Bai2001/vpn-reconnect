using System.Security.Cryptography;

namespace VpnReconnect.Updates;

/// <summary>下载包的 SHA256 校验。</summary>
public static class HashVerifier
{
    public static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    /// <summary>校验和文件内容形如「&lt;hash&gt;  VpnReconnect-1.0.2-win-x64.zip」。</summary>
    public static string? ParseHash(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var first = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (first.Length == 0)
        {
            return null;
        }

        var hash = first[0].Trim();
        return hash.Length == 64 && hash.All(Uri.IsHexDigit) ? hash.ToLowerInvariant() : null;
    }

    public static bool Matches(string path, string? expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex))
        {
            return false;
        }

        return string.Equals(ComputeFileHash(path), expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
