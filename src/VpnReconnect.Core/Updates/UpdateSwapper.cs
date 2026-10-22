using System.IO.Compression;

namespace VpnReconnect.Updates;

/// <summary>
/// 用「把旧 exe 改名 + 放入新 exe」的方式替换正在运行的程序文件：
/// Windows 允许给运行中的 exe 改名，但不允许覆盖删除，所以不能直接写回原路径。
/// </summary>
public static class UpdateSwapper
{
    public const string BackupSuffix = ".old";
    public const string ExeEntryName = "VpnReconnect.exe";

    public static string BackupPathFor(string exePath)
    {
        return exePath + BackupSuffix;
    }

    /// <summary>把 zip 里的 exe 解出来，返回解出的文件路径。</summary>
    public static string ExtractExe(string zipPath, string targetPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry(ExeEntryName)
            ?? zip.Entries.FirstOrDefault(item => item.Name.Equals(ExeEntryName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"压缩包里没有 {ExeEntryName}");

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        entry.ExtractToFile(targetPath, overwrite: true);
        return targetPath;
    }

    /// <summary>替换当前 exe；失败时把旧文件改回去。</summary>
    public static void Replace(string currentExePath, string newExePath)
    {
        if (!File.Exists(currentExePath))
        {
            throw new FileNotFoundException("找不到当前程序文件", currentExePath);
        }

        if (!File.Exists(newExePath))
        {
            throw new FileNotFoundException("找不到下载好的新版本文件", newExePath);
        }

        var backup = BackupPathFor(currentExePath);
        if (File.Exists(backup))
        {
            File.Delete(backup);
        }

        File.Move(currentExePath, backup);
        try
        {
            File.Copy(newExePath, currentExePath);
        }
        catch
        {
            // 回滚，保证程序还能启动
            try
            {
                if (File.Exists(currentExePath))
                {
                    File.Delete(currentExePath);
                }

                File.Move(backup, currentExePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            throw;
        }
    }

    /// <summary>启动后清掉上次更新留下的 .old（旧进程可能还没退干净，删不掉就算了）。</summary>
    public static bool TryCleanupBackup(string exePath)
    {
        try
        {
            var backup = BackupPathFor(exePath);
            if (!File.Exists(backup))
            {
                return true;
            }

            File.Delete(backup);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
