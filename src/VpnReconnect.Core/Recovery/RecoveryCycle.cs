namespace VpnReconnect.Recovery;

public sealed record RecoveryEvent(string Step, string Result, string Evidence);

public sealed class RecoveryCycle
{
    public bool AuthBlocked { get; private set; }

    public void NotePasswordSaved()
    {
        AuthBlocked = false;
    }

    public IReadOnlyList<RecoveryEvent> RunOnce(
        IRecoveryIo io,
        string connectionName,
        string username,
        string password)
    {
        var events = new List<RecoveryEvent>();

        if (io.IsHealthy())
        {
            events.Add(new RecoveryEvent("检查", "已连通", "已连通，不拨号不重启"));
            return events;
        }

        if (AuthBlocked)
        {
            events.Add(new RecoveryEvent("拨号", "已停止", "认证失败已阻断，本轮不拨号不重启"));
            return events;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            events.Add(new RecoveryEvent("拨号", "未提交用户名", "用户名或密码为空白，未提交用户名，不拨号不重启"));
            return events;
        }

        if (io.HasResidualDial())
        {
            io.KillResidualAndDisconnect();
            events.Add(new RecoveryEvent("结束残留", "已结束", "发现残留拨号，已结束并断开"));
        }

        var first = io.Dial(connectionName, username, password);
        events.Add(DialEvent(1, first));
        if (first.Finish == DialFinish.Up)
        {
            return events;
        }

        if (IsAuthFailure(first))
        {
            BlockAuth(events, first);
            return events;
        }

        RestartOnce(io, events, first);
        var second = io.Dial(connectionName, username, password);
        events.Add(DialEvent(2, second));
        if (second.Finish != DialFinish.Up && IsAuthFailure(second))
        {
            BlockAuth(events, second);
        }

        return events;
    }

    private void RestartOnce(IRecoveryIo io, List<RecoveryEvent> events, DialReport first)
    {
        io.KillResidualAndDisconnect();
        events.Add(first.ErrorCode == 756
            ? new RecoveryEvent("结束残留", "已结束", "错误码 756，视为残留占线，进入重启，不重复拨号")
            : new RecoveryEvent("结束残留", "已结束", "拨号失败或挂死，已结束残留"));

        io.RestartRasManAndIkeext();
        events.Add(new RecoveryEvent("重启服务", "已重启", "已重启 RasMan 和 IKEEXT"));
        io.RestartL2tpMiniport();
        events.Add(new RecoveryEvent("重启网卡", "已重启", "已重启 L2TP 网卡"));
        if (!io.L2tpMiniportPresent())
        {
            io.RestartIpMiniports();
            events.Add(new RecoveryEvent("重启网卡", "已重启", "L2TP 网卡未回来，已重启 IP 小型端口"));
        }
    }

    private void BlockAuth(List<RecoveryEvent> events, DialReport report)
    {
        AuthBlocked = true;
        var code = report.ErrorCode is int value ? $"错误码 {value}" : "认证被拒绝";
        events.Add(new RecoveryEvent("认证", "已阻断", $"{code}，已停止自动重拨和重启"));
    }

    private static bool IsAuthFailure(DialReport report)
    {
        return report.Finish == DialFinish.AuthRejected || report.ErrorCode == 691;
    }

    private static RecoveryEvent DialEvent(int attempt, DialReport report)
    {
        var submitted = report.UsernameSubmitted ? "已提交用户名和密码" : "未提交用户名";
        var detail = report.Finish == DialFinish.Hung && report.ErrorCode is null
            ? "超时无结果"
            : report.ErrorCode is int value
                ? $"错误码 {value}"
                : "无错误码";
        var which = attempt == 1 ? "第一次" : "第二次";
        return new RecoveryEvent("拨号", DialResult(report), $"{which}拨号，{submitted}，{detail}");
    }

    private static string DialResult(DialReport report)
    {
        return report.Finish switch
        {
            DialFinish.Up => "成功",
            DialFinish.AuthRejected => "认证拒绝",
            DialFinish.Hung => "挂死",
            _ => "失败",
        };
    }
}
