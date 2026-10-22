using VpnReconnect.Startup;

namespace VpnReconnect.App;

/// <summary>注册/删除「登录时自动启动」的计划任务（需要管理员权限）。</summary>
public static class LoginTaskRegistrar
{
    public const string TaskName = "VpnReconnect";

    /// <summary>创建或删除计划任务；成功返回 null，失败返回可读原因（含 schtasks 原始输出）。</summary>
    public static string? Apply(LoginTaskSpec spec, bool enabled)
    {
        if (!enabled)
        {
            var (deleted, deleteOutput) = Run("/Delete", "/TN", TaskName, "/F");
            if (deleted || deleteOutput.Contains("cannot find", StringComparison.OrdinalIgnoreCase)
                || deleteOutput.Contains("找不到", StringComparison.Ordinal))
            {
                return null;
            }

            return $"删除计划任务失败：{Summarize(deleteOutput)}";
        }

        if (!string.Equals(spec.Trigger, "OnLogon", StringComparison.Ordinal))
        {
            return $"不支持的登录触发器：{spec.Trigger}";
        }

        var level = spec.HighestPrivileges ? "HIGHEST" : "LIMITED";
        var command = string.IsNullOrWhiteSpace(spec.Arguments)
            ? $"\"{spec.ExePath}\""
            : $"\"{spec.ExePath}\" {spec.Arguments}";
        var (created, output) = Run(
            "/Create",
            "/F",
            "/TN",
            TaskName,
            "/SC",
            "ONLOGON",
            "/RL",
            level,
            "/IT",
            "/TR",
            command);

        return created ? null : Summarize(output);
    }

    /// <summary>是否已经存在同名计划任务（无需管理员即可查询）。</summary>
    public static bool Exists()
    {
        var (ok, _) = Run("/Query", "/TN", TaskName);
        return ok;
    }

    /// <summary>已存在任务指向的程序路径（取不到返回 null）。</summary>
    public static string? RegisteredExePath()
    {
        var (ok, output) = Run("/Query", "/TN", TaskName, "/FO", "LIST", "/V");
        if (!ok)
        {
            return null;
        }

        foreach (var line in output.Split('\n'))
        {
            var text = line.Trim();
            if (text.Contains("Task To Run", StringComparison.OrdinalIgnoreCase)
                || text.Contains("要运行的任务", StringComparison.Ordinal))
            {
                var start = text.IndexOf('"');
                if (start < 0)
                {
                    continue;
                }

                var end = text.IndexOf('"', start + 1);
                return end > start ? text[(start + 1)..end] : text[(start + 1)..];
            }
        }

        return null;
    }

    private static string Summarize(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var text = line.Trim();
            if (text.Length > 0 && !text.StartsWith("SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
        }

        return "schtasks 返回失败但没有输出";
    }

    private static (bool Ok, string Output) Run(params string[] args)
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            foreach (var arg in args)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            process.Start();
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(20_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }

                return (false, "schtasks 执行超时");
            }

            var combined = standardOutput + "\n" + standardError;
            return (process.ExitCode == 0, combined.Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return (false, ex.Message);
        }
    }
}
