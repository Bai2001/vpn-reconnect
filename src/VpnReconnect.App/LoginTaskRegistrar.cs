using VpnReconnect.Startup;

namespace VpnReconnect.App;

public static class LoginTaskRegistrar
{
    public const string TaskName = "VpnReconnect";

    public static void Apply(LoginTaskSpec spec, bool enabled)
    {
        if (!enabled)
        {
            Run(ignoreFailure: true, "/Delete", "/TN", TaskName, "/F");
            return;
        }

        if (!string.Equals(spec.Trigger, "OnLogon", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"不支持的登录触发器：{spec.Trigger}");
        }

        var level = spec.HighestPrivileges ? "HIGHEST" : "LIMITED";
        var command = string.IsNullOrWhiteSpace(spec.Arguments)
            ? $"\"{spec.ExePath}\""
            : $"\"{spec.ExePath}\" {spec.Arguments}";
        Run(
            ignoreFailure: false,
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
    }

    private static void Run(bool ignoreFailure, params string[] args)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        if (!process.WaitForExit(20_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }

            if (!ignoreFailure)
            {
                throw new InvalidOperationException("计划任务操作超时");
            }

            return;
        }

        if (process.ExitCode != 0 && !ignoreFailure)
        {
            throw new InvalidOperationException($"计划任务操作失败，退出码 {process.ExitCode}");
        }
    }
}
