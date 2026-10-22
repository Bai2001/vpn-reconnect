using System.Net.Http;
using System.Text.Json;
using VpnReconnect.Updates;

namespace VpnReconnect.App;

/// <summary>从 GitHub Releases 拿最新版本信息并下载资产。</summary>
public sealed class GitHubUpdateClient : IDisposable
{
    public const string Repository = "Bai2001/vpn-reconnect";

    private static readonly Uri LatestReleaseUri =
        new($"https://api.github.com/repos/{Repository}/releases/latest", UriKind.Absolute);

    private readonly HttpClient _http;

    public GitHubUpdateClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("VpnReconnect-Updater");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(LatestReleaseUri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GitHub 返回 HTTP {(int)response.StatusCode}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (!ReleaseVersion.TryParse(tag, out var version))
        {
            return null;
        }

        var pageUrl = root.TryGetProperty("html_url", out var pageElement) ? pageElement.GetString() ?? "" : "";
        var notes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? "" : "";

        ReleaseAsset? package = null;
        ReleaseAsset? checksum = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
                var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() ?? "" : "";
                var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes) ? bytes : 0;
                if (name.Length == 0 || url.Length == 0)
                {
                    continue;
                }

                if (name.EndsWith(".zip.sha256", StringComparison.OrdinalIgnoreCase))
                {
                    checksum ??= new ReleaseAsset(name, url, size);
                }
                else if (name.EndsWith(UpdateEvaluator.PackageSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    package ??= new ReleaseAsset(name, url, size);
                }
            }
        }

        return new ReleaseInfo(tag ?? version.ToString(), version, notes, pageUrl, package, checksum);
    }

    public async Task DownloadAsync(
        string url,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");

        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"下载失败，HTTP {(int)response.StatusCode}");
        }

        var total = response.Content.Headers.ContentLength ?? -1;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long received = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            if (total > 0)
            {
                progress?.Report(Math.Min(1d, (double)received / total));
            }
        }
    }

    public async Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
            : null;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
