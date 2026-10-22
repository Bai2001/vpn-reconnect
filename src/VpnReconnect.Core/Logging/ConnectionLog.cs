using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace VpnReconnect.Logging;

public sealed record LogRow(DateTimeOffset Time, string Step, string Result, string Evidence);

public sealed class ConnectionLog
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly List<string> _secrets = new();

    public ConnectionLog(string path, Func<DateTimeOffset> clock)
    {
        _path = path;
        _clock = clock;
    }

    public void Append(string step, string result, string evidence, string? secret)
    {
        lock (_gate)
        {
            Remember(secret);
            var row = new LogRow(_clock(), step, result, Redact(evidence));
            EnsureParent(_path);
            File.AppendAllText(_path, Serialize(row) + "\n", Utf8);
        }
    }

    public IReadOnlyList<LogRow> ReadAll()
    {
        lock (_gate)
        {
            return ReadRows();
        }
    }

    /// <summary>
    /// 只读最近的若干条记录，从文件尾部回读，不解析整个日志。
    /// 界面定时刷新用这个，避免日志积累到几万行后每次刷新都全量解析。
    /// </summary>
    public IReadOnlyList<LogRow> ReadTail(int maxRows)
    {
        lock (_gate)
        {
            if (maxRows <= 0)
            {
                return Array.Empty<LogRow>();
            }

            var rows = new List<LogRow>();
            foreach (var line in ReadTailLines(maxRows))
            {
                if (TryParse(line, out var row))
                {
                    rows.Add(row);
                }
            }

            return rows;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            EnsureParent(_path);
            File.WriteAllText(_path, string.Empty, Utf8);
        }
    }

    public void ExportTo(string destination)
    {
        lock (_gate)
        {
            EnsureParent(destination);
            if (!File.Exists(_path))
            {
                File.WriteAllText(destination, string.Empty, Utf8);
                return;
            }

            var text = File.ReadAllText(_path, Utf8);
            if (ContainsSecret(text))
            {
                text = SerializeRows(ReadRows().Select(RedactRow).ToList());
            }

            File.WriteAllText(destination, text, Utf8);
        }
    }

    public void Rotate(long maxBytes, TimeSpan maxAge)
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return;
            }

            if (maxBytes < 0)
            {
                maxBytes = 0;
            }

            // 快速退出：体积没超且最旧一行没过期时，不必全量解析并重写整个文件
            if (new FileInfo(_path).Length <= maxBytes && !HasExpiredRow(maxAge))
            {
                return;
            }

            var now = _clock();
            var kept = new List<LogRow>();
            foreach (var row in ReadRows())
            {
                if (now - row.Time <= maxAge)
                {
                    kept.Add(row);
                }
            }

            while (kept.Count > 0 && EncodedSize(kept) > maxBytes)
            {
                var oldest = 0;
                for (var i = 1; i < kept.Count; i++)
                {
                    if (kept[i].Time < kept[oldest].Time)
                    {
                        oldest = i;
                    }
                }

                kept.RemoveAt(oldest);
            }

            File.WriteAllText(_path, SerializeRows(kept), Utf8);
        }
    }

    private void Remember(string? secret)
    {
        if (string.IsNullOrEmpty(secret) || _secrets.Contains(secret))
        {
            return;
        }

        _secrets.Add(secret);
    }

    private LogRow RedactRow(LogRow row) => row with { Evidence = Redact(row.Evidence) };

    private string Redact(string text)
    {
        if (string.IsNullOrEmpty(text) || _secrets.Count == 0)
        {
            return text;
        }

        foreach (var secret in _secrets.OrderByDescending(item => item.Length))
        {
            text = text.Replace(secret, "***", StringComparison.Ordinal);
        }

        return text;
    }

    private bool ContainsSecret(string text)
    {
        foreach (var secret in _secrets)
        {
            if (text.Contains(secret, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private List<LogRow> ReadRows()
    {
        var rows = new List<LogRow>();
        if (!File.Exists(_path))
        {
            return rows;
        }

        foreach (var line in File.ReadLines(_path, Utf8))
        {
            if (TryParse(line, out var row))
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    private bool HasExpiredRow(TimeSpan maxAge)
    {
        if (!File.Exists(_path))
        {
            return false;
        }

        using var reader = new StreamReader(_path, Utf8);
        while (reader.ReadLine() is { } line)
        {
            if (TryParse(line, out var row))
            {
                return _clock() - row.Time > maxAge;
            }
        }

        return false;
    }

    private bool TryParse(string line, out LogRow row)
    {
        row = null!;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<LogRow>(line, JsonOptions);
            if (parsed is null)
            {
                return false;
            }

            row = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>从文件末尾按块回读，凑够 maxRows + 1 行就停，落到行边界后再解码。</summary>
    private List<string> ReadTailLines(int maxRows)
    {
        var lines = new List<string>();
        if (!File.Exists(_path))
        {
            return lines;
        }

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        if (length == 0)
        {
            return lines;
        }

        const int chunkSize = 32 * 1024;
        var wanted = maxRows + 1;
        var newlines = 0;
        long position = length;
        var buffer = new byte[chunkSize];
        var chunks = new List<byte[]>();
        while (position > 0 && newlines < wanted)
        {
            var size = (int)Math.Min(chunkSize, position);
            position -= size;
            stream.Position = position;
            stream.ReadExactly(buffer, 0, size);
            var chunk = new byte[size];
            Array.Copy(buffer, chunk, size);
            chunks.Insert(0, chunk);
            for (var i = 0; i < size; i++)
            {
                if (chunk[i] == (byte)'\n')
                {
                    newlines++;
                }
            }
        }

        var total = 0;
        foreach (var chunk in chunks)
        {
            total += chunk.Length;
        }

        var text = new byte[total];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Array.Copy(chunk, 0, text, offset, chunk.Length);
            offset += chunk.Length;
        }

        var split = Utf8.GetString(text).Split('\n');
        var complete = split.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
        if (complete.Count <= maxRows)
        {
            return complete;
        }

        return complete.GetRange(complete.Count - maxRows, maxRows);
    }

    private static string Serialize(LogRow row) => JsonSerializer.Serialize(row, JsonOptions);

    private static string SerializeRows(IReadOnlyList<LogRow> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            builder.Append(Serialize(row));
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static long EncodedSize(IReadOnlyList<LogRow> rows)
    {
        long size = 0;
        foreach (var row in rows)
        {
            size += Utf8.GetByteCount(Serialize(row));
            size += 1;
        }

        return size;
    }

    private static void EnsureParent(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
