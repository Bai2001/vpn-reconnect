using System.Net.Http;
using System.Text.Json;
using VpnReconnect.Logging;
using VpnReconnect.Updates;

namespace VpnReconnect.App;

public enum UpdateStage
{
    Idle = 0,
    Checking = 1,
    Available = 2,
    Downloading = 3,
    Installing = 4,

    /// <summary>检查或安装失败，可由用户重试。</summary>
    Failed = 5,
}

public sealed record UpdateState(
    UpdateStage Stage,
    string CurrentVersion,
    string? LatestVersion,
    bool CanInstall,
    double Progress,
    string Message);

/// <summary>
/// 半自动更新：只负责查版本、下载并校验，替换与重启交给 AppHost，
/// 由用户点「立即更新」才动手。
/// </summary>
public sealed class UpdateCoordinator : IDisposable
{
    private readonly GitHubUpdateClient _client = new();
    private readonly ConnectionLog _log;
    private readonly Func<ReleaseVersion> _currentVersion;
    private readonly string _workDirectory;
    private readonly object _gate = new();

    private UpdateStage _stage = UpdateStage.Idle;
    private ReleaseInfo? _latest;
    private string _message = "尚未检查更新";
    private double _progress;

    public UpdateCoordinator(ConnectionLog log, Func<ReleaseVersion> currentVersion, string workDirectory)
    {
        _log = log;
        _currentVersion = currentVersion;
        _workDirectory = workDirectory;
    }

    public UpdateState Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new UpdateState(
                    _stage,
                    _currentVersion().ToString(),
                    _latest?.Version.ToString(),
                    _latest is not null && UpdateEvaluator.CanInstall(_latest),
                    _progress,
                    _message);
            }
        }
    }

    public bool Busy
    {
        get
        {
            lock (_gate)
            {
                return _stage is UpdateStage.Checking or UpdateStage.Downloading or UpdateStage.Installing;
            }
        }
    }

    /// <summary>检查最新版本；返回是否成功拿到结果。</summary>
    public async Task<bool> CheckAsync(bool manual, CancellationToken cancellationToken)
    {
        var current = _currentVersion();
        lock (_gate)
        {
            if (_stage is UpdateStage.Downloading or UpdateStage.Installing)
            {
                return false;
            }

            _stage = UpdateStage.Checking;
            _message = "正在检查更新…";
            _progress = 0;
        }

        try
        {
            var release = await _client.GetLatestAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (release is null)
                {
                    _latest = null;
                    _stage = UpdateStage.Idle;
                    _message = $"已是最新版本 v{current}";
                }
                else if (UpdateEvaluator.IsNewer(current, release))
                {
                    _latest = release;
                    _stage = UpdateStage.Available;
                    _message = UpdateEvaluator.CanInstall(release)
                        ? $"发现新版本 v{release.Version}，可一键更新"
                        : $"发现新版本 v{release.Version}，请到 Release 页面手动下载";
                }
                else
                {
                    _latest = null;
                    _stage = UpdateStage.Idle;
                    _message = $"已是最新版本 v{current}";
                }

                return true;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            lock (_gate)
            {
                _stage = UpdateStage.Failed;
                _message = manual ? $"检查更新失败：{ex.Message}" : "自动检查更新失败，稍后自动重试";
            }

            _log.Append("更新", "检查失败", ex.Message, null);
            return false;
        }
    }

    /// <summary>下载 zip、校验 sha256、解出 exe；成功返回 exe 路径，失败返回 null。</summary>
    public async Task<string?> PrepareAsync(CancellationToken cancellationToken)
    {
        ReleaseInfo release;
        lock (_gate)
        {
            if (_latest is not { } found || !UpdateEvaluator.CanInstall(found))
            {
                return null;
            }

            release = found;
            _stage = UpdateStage.Downloading;
            _progress = 0;
            _message = $"正在下载 v{release.Version}…";
        }

        var version = release.Version.ToString();
        var zipPath = Path.Combine(_workDirectory, $"VpnReconnect-{version}.zip");
        var exePath = Path.Combine(_workDirectory, UpdateSwapper.ExeEntryName);

        try
        {
            Directory.CreateDirectory(_workDirectory);
            var progress = new Progress<double>(value =>
            {
                lock (_gate)
                {
                    _progress = value;
                    _message = $"正在下载 v{version}… {value * 100:0}%";
                }
            });

            await _client
                .DownloadAsync(release.Package!.DownloadUrl, zipPath, progress, cancellationToken)
                .ConfigureAwait(false);

            lock (_gate)
            {
                _stage = UpdateStage.Installing;
                _message = "正在校验安装包…";
            }

            var checksumText = await _client
                .GetStringAsync(release.PackageChecksum!.DownloadUrl, cancellationToken)
                .ConfigureAwait(false);
            var expected = HashVerifier.ParseHash(checksumText ?? "");
            if (expected is null)
            {
                throw new InvalidDataException("下载的校验和文件无法解析");
            }

            if (!HashVerifier.Matches(zipPath, expected))
            {
                throw new InvalidDataException("安装包校验失败（sha256 不匹配），已放弃更新");
            }

            var extracted = UpdateSwapper.ExtractExe(zipPath, exePath);
            lock (_gate)
            {
                _progress = 1;
                _message = $"v{version} 已准备好，正在替换程序文件…";
            }

            _log.Append("更新", "下载完成", $"v{version} 已下载并通过 sha256 校验", null);
            return extracted;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
            or TaskCanceledException or UnauthorizedAccessException)
        {
            lock (_gate)
            {
                _stage = UpdateStage.Failed;
                _message = $"更新失败：{ex.Message}";
            }

            _log.Append("更新", "下载失败", ex.Message, null);
            TryDelete(zipPath);
            return null;
        }
    }

    public void MarkFailed(string message)
    {
        lock (_gate)
        {
            _stage = UpdateStage.Failed;
            _message = message;
        }
    }

    public void MarkRestarting(string version)
    {
        lock (_gate)
        {
            _stage = UpdateStage.Installing;
            _message = $"已更新到 v{version}，正在重启…";
        }
    }

    /// <summary>清掉一天前留下的下载包和临时 exe，别一直占着几十兆空间。</summary>
    public static void PurgeOldFiles(string workDirectory)
    {
        try
        {
            if (!Directory.Exists(workDirectory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(workDirectory))
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(1))
                {
                    TryDelete(file);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
