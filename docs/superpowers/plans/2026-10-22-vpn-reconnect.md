# 公司 VPN 重连软件实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 做一个登录后后台运行的 Windows 程序，用自存账号密码重连现有 L2TP「公司」，并在挂死时重启 RasMan、IKEEXT 和 L2TP 网卡。

**Architecture:** 判定、恢复、凭据和日志放在 `net8.0` 类库里，用接口替掉 ping、拨号和重启。WPF 程序只负责窗口、RasDial、DPAPI、计划任务和单实例。恢复每轮最多拨两次，第二次之前才允许重启服务。

**Tech Stack:** .NET 8、C#、WPF、xUnit、RasDial、DPAPI、任务计划程序。

**Spec:** `docs/superpowers/specs/2026-10-22-vpn-reconnect-design.md`

## Global Constraints

- 界面、日志、错误提示使用中文。
- 不新建或修改 VPN 电话簿、分流和固定路由。
- 不调用裸 `rasdial.exe 连接名`。拨号必须提交软件自存的用户名和密码。
- 已连接时不重启 RasMan、IKEEXT 或任何网卡。
- 不把 `101.207.248.238` 当作隐式连通目标。只有用户填进目标地址时才 ping 该字符串。
- WAN Miniport 设备存在不等于已连接。`0.0.0.0` 和 `169.254.0.0/16` 不是有效隧道地址。
- 不自动猜测 VPN 适配器。未填目标地址时，只看用户在设置里选定的接口名；没选则未连接。
- 错误码 691 后停止自动拨号和重启，直到用户保存新密码。
- 密码不出现在命令行、日志、导出文件中。
- 检查间隔默认 60 秒，允许 15 到 600 秒。
- 每一轮恢复最多重启一次服务和 L2TP 网卡，然后最多再拨一次。
- 关闭窗口只隐藏；只有退出才停止本次检查。
- 未获用户确认不得 `git commit`。不要按任务频繁提交。
- 不要运行发布打包。验证用 `dotnet test`。

## Review Focus

- 用户没选适配器，或选定的接口当前没有有效 IPv4：未设目标时必须判为未连接。测试在任务 2。
- 「公司」适配器地址是 `0.0.0.0` 或 `169.254.1.1`：必须判为未连接。测试在任务 2。
- 已经连通时调用恢复：不得拨号，不得重启。测试在任务 4。
- 拨号返回 691：不得重启，下一轮在保存新密码前不得再拨。测试在任务 4。
- 关闭窗口后检查器仍运行，退出后停止。测试在任务 7。

---

## 文件结构

- `src/VpnReconnect.Core/`：设置、连通判定、凭据抽象、恢复循环、日志、检查服务。无 WPF、无 RasDial。
- `src/VpnReconnect.App/`：WPF 三页、RasDial 拨号、服务与网卡重启、DPAPI、计划任务、单实例。
- `tests/VpnReconnect.Tests/`：只引用 Core。
- `docs/superpowers/specs/2026-10-22-vpn-reconnect-design.md`：已有规格，不改行为。

### Task 1: 解决方案与设置校验

**Files:**
- Create: `vpn-reconnect.sln`
- Create: `src/VpnReconnect.Core/VpnReconnect.Core.csproj`
- Create: `src/VpnReconnect.Core/Settings/AppSettings.cs`
- Create: `src/VpnReconnect.Core/Settings/SettingsValidator.cs`
- Create: `tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj`
- Create: `tests/VpnReconnect.Tests/SettingsValidatorTests.cs`
- Create: `.gitignore`
- Test: `tests/VpnReconnect.Tests/SettingsValidatorTests.cs`

**Interfaces:**
- Consumes: 无
- Produces: `AppSettings(string ConnectionName, string? TargetAddress, string? SelectedAdapterName, string Username, int IntervalSeconds, bool StartOnLogin)`；`SettingsValidator.Validate(AppSettings settings) -> string?`，合法返回 `null`，否则返回中文原因。`SelectedAdapterName` 允许为空。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void 间隔14秒被拒绝()
{
    var error = SettingsValidator.Validate(new AppSettings("公司", null, null, "bjj", 14, true));
    Assert.NotNull(error);
}

[Fact]
public void 间隔15和600秒通过()
{
    Assert.Null(SettingsValidator.Validate(new AppSettings("公司", "192.168.1.21", "公司", "bjj", 15, true)));
    Assert.Null(SettingsValidator.Validate(new AppSettings("公司", null, null, "bjj", 600, false)));
}

[Fact]
public void 空连接名被拒绝()
{
    Assert.NotNull(SettingsValidator.Validate(new AppSettings("  ", null, null, "bjj", 60, true)));
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj --filter FullyQualifiedName~SettingsValidatorTests`
Expected: 编译失败或测试失败，因为类型尚不存在。

- [ ] **Step 3: 建立 net8.0 类库和 xUnit 测试项目，实现上述签名**

连接名去掉空白后为空则拒绝。间隔小于 15 或大于 600 则拒绝。目标地址允许 null 或空白，空白保存前规范成 null。用户名允许暂空，不在这一层拒绝。`.gitignore` 忽略 `bin/`、`obj/`、`.superpowers/`、`*.user`、凭据文件 `credentials.bin`。

- [ ] **Step 4: 运行测试，确认通过**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj --filter FullyQualifiedName~SettingsValidatorTests`
Expected: PASS

- [ ] **Step 5: 不提交**

本任务不单独提交。

### Task 2: 连通判定

**Files:**
- Create: `src/VpnReconnect.Core/Health/IPing.cs`
- Create: `src/VpnReconnect.Core/Health/AdapterSnapshot.cs`
- Create: `src/VpnReconnect.Core/Health/ConnectivityJudge.cs`
- Test: `tests/VpnReconnect.Tests/ConnectivityJudgeTests.cs`

**Interfaces:**
- Consumes: 无
- Produces: `IPing.Ping(string address, int attempts, TimeSpan perAttemptTimeout) -> bool`；`AdapterSnapshot(string ConnectionName, string? IPv4)`；`HealthVerdict(bool Connected, string Evidence, string? AdapterIPv4)`；`ConnectivityJudge.Judge(string? targetAddress, string? selectedAdapterName, AdapterSnapshot? selectedAdapter, IPing ping) -> HealthVerdict`。`attempts` 固定传 2，`perAttemptTimeout` 固定为 1 秒。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void 未设目标且没有公司适配器地址时未连接()
{
    var ping = new FakePing(always: true);
    var verdict = ConnectivityJudge.Judge(null, null, null, ping);
    Assert.False(verdict.Connected);
    Assert.Equal(0, ping.Calls);
}

[Fact]
public void 没选适配器时即使别处有地址也不连接()
{
    var ping = new FakePing(always: true);
    var verdict = ConnectivityJudge.Judge(null, null, new AdapterSnapshot("Mihomo", "198.18.0.1"), ping);
    Assert.False(verdict.Connected);
    Assert.Equal(0, ping.Calls);
}

[Fact]
public void 零地址和169地址不算拿到IP()
{
    var ping = new FakePing(always: true);
    Assert.False(ConnectivityJudge.Judge(null, "公司", new AdapterSnapshot("公司", "0.0.0.0"), ping).Connected);
    Assert.False(ConnectivityJudge.Judge(null, "公司", new AdapterSnapshot("公司", "169.254.1.1"), ping).Connected);
}

[Fact]
public void 未设目标时只认公司适配器的有效IPv4()
{
    var ping = new FakePing(always: true);
    var verdict = ConnectivityJudge.Judge(null, "公司", new AdapterSnapshot("公司", "192.168.4.28"), ping);
    Assert.True(verdict.Connected);
    Assert.Equal("192.168.4.28", verdict.AdapterIPv4);
    Assert.Equal(0, ping.Calls);
}

[Fact]
public void 设了目标时只ping该地址()
{
    var ping = new FakePing(succeedsFor: "192.168.1.21");
    var up = ConnectivityJudge.Judge("192.168.1.21", "公司", new AdapterSnapshot("公司", null), ping);
    var down = ConnectivityJudge.Judge("192.168.1.22", "公司", new AdapterSnapshot("公司", "192.168.4.28"), ping);
    Assert.True(up.Connected);
    Assert.False(down.Connected);
    Assert.DoesNotContain(ping.Asked, a => a == "101.207.248.238");
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj --filter FullyQualifiedName~ConnectivityJudgeTests`
Expected: FAIL，类型不存在。

- [ ] **Step 3: 实现 `ConnectivityJudge.Judge`**

签名是 `Judge(string? targetAddress, string? selectedAdapterName, AdapterSnapshot? selectedAdapter, IPing ping)`。没选适配器，或快照名字与所选名字不同，都当成没有选定适配器。目标地址去掉空白后非空时，只 ping 该地址。目标为空时不调用 ping；仅当所选接口当前 IPv4 非空、非 `0.0.0.0`、非 `169.254.*` 才算连接。`Evidence` 用中文写明 ping 结果、所选接口名，或「未选择适配器」「适配器无有效 IPv4」。

- [ ] **Step 4: 运行测试，确认通过**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj --filter FullyQualifiedName~ConnectivityJudgeTests`
Expected: PASS

- [ ] **Step 5: 不提交**

### Task 3: 凭据存储

**Files:**
- Create: `src/VpnReconnect.Core/Credentials/ISecretProtector.cs`
- Create: `src/VpnReconnect.Core/Credentials/CredentialStore.cs`
- Test: `tests/VpnReconnect.Tests/CredentialStoreTests.cs`

**Interfaces:**
- Consumes: 无
- Produces: `ISecretProtector.Protect(byte[] plaintext) -> byte[]`，`Unprotect(byte[] blob) -> byte[]`；`CredentialStore(string path, ISecretProtector protector)`；`Save(string username, string password)`；`Load() -> (string Username, string Password)?`；文件不存在时 `Load` 返回 null。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void 保存后能读回且磁盘上没有明文密码()
{
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
    var store = new CredentialStore(path, new XorProtector(0x5A));
    store.Save("bjj", "secret-value");
    var loaded = store.Load();
    Assert.Equal(("bjj", "secret-value"), loaded);
    var disk = File.ReadAllText(path);
    Assert.DoesNotContain("secret-value", disk);
    File.Delete(path);
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj --filter FullyQualifiedName~CredentialStoreTests`
Expected: FAIL，类型不存在。

- [ ] **Step 3: 实现 `CredentialStore`**

用 UTF-8 JSON 放用户名和密码，再交给 `ISecretProtector`。磁盘只写保护后的字节。测试用的 `XorProtector` 放在测试项目，不放进 Core。

- [ ] **Step 4: 运行测试，确认通过**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj --filter FullyQualifiedName~CredentialStoreTests`
Expected: PASS

- [ ] **Step 5: 不提交**

### Task 4: 恢复循环

**Files:**
- Create: `src/VpnReconnect.Core/Recovery/IRecoveryIo.cs`
- Create: `src/VpnReconnect.Core/Recovery/RecoveryCycle.cs`
- Test: `tests/VpnReconnect.Tests/RecoveryCycleTests.cs`

**Interfaces:**
- Consumes: 无
- Produces: `DialFinish` 枚举 `Up`、`AuthRejected`、`Failed`、`Hung`；`DialReport(DialFinish Finish, int? ErrorCode, bool UsernameSubmitted)`；`IRecoveryIo` 方法 `IsHealthy()`、`HasResidualDial()`、`KillResidualAndDisconnect()`、`Dial(string connectionName, string username, string password) -> DialReport`、`L2tpMiniportPresent()`、`RestartRasManAndIkeext()`、`RestartL2tpMiniport()`、`RestartIpMiniports()`；`RecoveryEvent(string Step, string Result, string Evidence)`；`RecoveryCycle.AuthBlocked`；`NotePasswordSaved()`；`RunOnce(IRecoveryIo io, string connectionName, string username, string password) -> IReadOnlyList<RecoveryEvent>`。`Dial` 由实现者阻塞最多 30 秒后返回，循环本身不再等待。

- [ ] **Step 1: 写失败测试**

测试用假的 `IRecoveryIo` 记录调用顺序。

```csharp
[Fact]
public void 已连通时不拨号不重启()
{
    var io = new FakeIo { Healthy = true };
    new RecoveryCycle().RunOnce(io, "公司", "bjj", "pw");
    Assert.Empty(io.Dials);
    Assert.Equal(0, io.Restarts);
}

[Fact]
public void 第一次拨通就不再重启()
{
    var io = new FakeIo { HealthyAfterDials = 1 };
    new RecoveryCycle().RunOnce(io, "公司", "bjj", "pw");
    Assert.Single(io.Dials);
    Assert.Equal(0, io.Restarts);
    Assert.True(io.Dials[0].UsernameSubmitted);
}

[Fact]
public void 第一次628后只重启一次并再拨一次()
{
    var io = new FakeIo { First = new DialReport(DialFinish.Failed, 628, true) };
    new RecoveryCycle().RunOnce(io, "公司", "bjj", "pw");
    Assert.Equal(2, io.Dials.Count);
    Assert.Equal(1, io.Restarts);
    Assert.Equal(1, io.L2tpRestarts);
}

[Fact]
public void 691后不再重启且下一轮不拨()
{
    var io = new FakeIo { First = new DialReport(DialFinish.AuthRejected, 691, true) };
    var cycle = new RecoveryCycle();
    cycle.RunOnce(io, "公司", "bjj", "pw");
    Assert.True(cycle.AuthBlocked);
    Assert.Equal(0, io.Restarts);
    io.Dials.Clear();
    cycle.RunOnce(io, "公司", "bjj", "pw");
    Assert.Empty(io.Dials);
    cycle.NotePasswordSaved();
    io.First = new DialReport(DialFinish.Up, null, true);
    cycle.RunOnce(io, "公司", "bjj", "pw");
    Assert.Single(io.Dials);
}

[Fact]
public void 挂死时先结束残留再重启()
{
    var io = new FakeIo { First = new DialReport(DialFinish.Hung, null, true) };
    new RecoveryCycle().RunOnce(io, "公司", "bjj", "pw");
    Assert.Contains("Kill", io.Order);
    Assert.True(io.Order.IndexOf("Kill") < io.Order.IndexOf("Restart"));
    Assert.Equal(1, io.Restarts);
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj --filter FullyQualifiedName~RecoveryCycleTests`
Expected: FAIL，类型不存在。

- [ ] **Step 3: 实现 `RecoveryCycle.RunOnce`**

开始时若 `IsHealthy()` 为真，只记一条连通事件并返回。用户名或密码为空白时不调用 `Dial`，记「未提交用户名」，不重启。若 `HasResidualDial()` 为真，先 `KillResidualAndDisconnect()`。第一次 `Dial` 返回 `Up` 则结束。返回 `AuthRejected` 或 `ErrorCode == 691` 时设 `AuthBlocked`，不重启。`ErrorCode == 756` 视为残留占线：先 `KillResidualAndDisconnect()`，再把这次当成第一次失败，进入重启路径，不要对 756 死循环。`AuthBlocked` 为真时后续 `RunOnce` 不拨号，直到 `NotePasswordSaved()`。其他失败或 `Hung`：再杀残留，调用 `RestartRasManAndIkeext` 和 `RestartL2tpMiniport`；若 `L2tpMiniportPresent()` 为假，再调用 `RestartIpMiniports()`。然后第二次 `Dial`。第二次无论成败都结束本轮，不再重启。每次 `Dial` 的事件依据必须写明 `UsernameSubmitted`。假对象的 `Dials` 是 `List<DialReport>`，`Order` 按调用记 `Kill` 和 `Restart`。

- [ ] **Step 4: 运行测试，确认通过**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj --filter FullyQualifiedName~RecoveryCycleTests`
Expected: PASS

- [ ] **Step 5: 不提交**

### Task 5: 日志文件

**Files:**
- Create: `src/VpnReconnect.Core/Logging/ConnectionLog.cs`
- Test: `tests/VpnReconnect.Tests/ConnectionLogTests.cs`

**Interfaces:**
- Consumes: 无
- Produces: `LogRow(DateTimeOffset Time, string Step, string Result, string Evidence)`；`ConnectionLog(string path, Func<DateTimeOffset> clock)`；`Append(string step, string result, string evidence, string? secret)`；`ReadAll() -> IReadOnlyList<LogRow>`；`Clear()`；`ExportTo(string destination)`；`Rotate(long maxBytes, TimeSpan maxAge)`。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void 依据里的密码会被替换()
{
    var path = Path.GetTempFileName();
    var log = new ConnectionLog(path, () => DateTimeOffset.Parse("2026-10-22T09:00:14+08:00"));
    log.Append("拨号", "失败", "密码 secret-value 无效", "secret-value");
    Assert.DoesNotContain("secret-value", File.ReadAllText(path));
    var exported = Path.GetTempFileName();
    log.ExportTo(exported);
    Assert.DoesNotContain("secret-value", File.ReadAllText(exported));
    File.Delete(exported);
    File.Delete(path);
}

[Fact]
public void 清空后读不到旧记录()
{
    var path = Path.GetTempFileName();
    var log = new ConnectionLog(path, () => DateTimeOffset.UnixEpoch);
    log.Append("检查", "未连接", "适配器无有效 IPv4", null);
    log.Clear();
    Assert.Empty(log.ReadAll());
    File.Delete(path);
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj --filter FullyQualifiedName~ConnectionLogTests`
Expected: FAIL，类型不存在。

- [ ] **Step 3: 实现 `ConnectionLog`**

`secret` 非空时，写入和导出前把依据中的该字符串换成 `***`。`Rotate(5 * 1024 * 1024, TimeSpan.FromDays(7))` 删除超出体积或天数的最旧行。追加用 UTF-8 JSON Lines。

- [ ] **Step 4: 运行测试，确认通过**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj --filter FullyQualifiedName~ConnectionLogTests`
Expected: PASS

- [ ] **Step 5: 不提交**

### Task 6: 把判定、恢复和日志接成一次检查

**Files:**
- Create: `src/VpnReconnect.Core/Session/CheckService.cs`
- Test: `tests/VpnReconnect.Tests/CheckServiceTests.cs`

**Interfaces:**
- Consumes: `ConnectivityJudge.Judge`、`RecoveryCycle.RunOnce`、`ConnectionLog.Append`、`CredentialStore.Load`、`AppSettings`
- Produces: `IAdapterLookup.Get(string connectionName) -> AdapterSnapshot?`；`CheckService.CheckOnce() -> HealthVerdict`；`SavePassword(string username, string password)` 会调用 `RecoveryCycle.NotePasswordSaved()`。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void 未连接时日志能区分ping失败()
{
    var log = NewLog();
    var ping = new FakePing(succeedsFor: "none");
    var service = NewService(log, ping, target: "192.168.1.21", adapterIp: null);
    service.CheckOnce();
    Assert.Contains(log.ReadAll(), row => row.Evidence.Contains("192.168.1.21") && row.Result.Contains("未连接"));
}

[Fact]
public void 已连接时不进入恢复()
{
    var io = new FakeIo { Healthy = true };
    var service = NewService(NewLog(), new FakePing(always: true), target: null, adapterIp: "192.168.4.28", io);
    service.CheckOnce();
    Assert.Empty(io.Dials);
    Assert.Equal(0, io.Restarts);
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj --filter FullyQualifiedName~CheckServiceTests`
Expected: FAIL，类型不存在。

- [ ] **Step 3: 实现 `CheckService.CheckOnce`**

先按用户保存的接口名读取那一块 IPv4 接口，再调用 `Judge(targetAddress, selectedAdapterName, adapter, ping)`。设置页另有列表：枚举当前所有有 IPv4 的接口，显示接口名和地址，用户点选后只保存接口名。不要按 VPN、WAN Miniport 等关键词自动挑选。连通则只写检查日志，不调用 `RunOnce`。未连通则 `RunOnce`，并把每个 `RecoveryEvent` 写入日志，`secret` 传已加载的密码。没有保存的密码时不拨号，日志结果写「未提交用户名」。

- [ ] **Step 4: 运行测试，确认通过**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj`
Expected: PASS，此前全部测试仍通过。

- [ ] **Step 5: 不提交**

### Task 7: 窗口会话与开机任务

**Files:**
- Create: `src/VpnReconnect.Core/Session/SessionController.cs`
- Create: `src/VpnReconnect.Core/Startup/LoginTaskSpec.cs`
- Test: `tests/VpnReconnect.Tests/SessionControllerTests.cs`

**Interfaces:**
- Consumes: 无
- Produces: `SessionController.StartBackground()`；`ShowWindow()`；`CloseWindow()`；`Exit()`；属性 `CheckerRunning`、`WindowVisible`。`LoginTaskSpec.For(string exePath) -> (string Arguments, bool HighestPrivileges, string Trigger)`，`Arguments` 为 `--background`，`HighestPrivileges` 为 true，`Trigger` 为 `OnLogon`。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void 关闭窗口不停止检查_退出才停止()
{
    var session = new SessionController();
    session.StartBackground();
    session.ShowWindow();
    session.CloseWindow();
    Assert.False(session.WindowVisible);
    Assert.True(session.CheckerRunning);
    session.Exit();
    Assert.False(session.CheckerRunning);
}

[Fact]
public void 开机任务是登录时最高权限后台启动()
{
    var spec = LoginTaskSpec.For(@"C:\app\VpnReconnect.exe");
    Assert.Equal("--background", spec.Arguments);
    Assert.True(spec.HighestPrivileges);
    Assert.Equal("OnLogon", spec.Trigger);
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj --filter FullyQualifiedName~SessionControllerTests`
Expected: FAIL，类型不存在。

- [ ] **Step 3: 实现这两个类型**

`StartBackground` 使 `CheckerRunning` 为真且窗口不可见。`ShowWindow` 只改变可见性。`Exit` 把两者都清掉。这里不注册真实计划任务。

- [ ] **Step 4: 运行测试，确认通过**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj --filter FullyQualifiedName~SessionControllerTests`
Expected: PASS

- [ ] **Step 5: 不提交**

### Task 8: WPF 外壳与 Windows 拨号

**Files:**
- Create: `src/VpnReconnect.App/VpnReconnect.App.csproj`
- Create: `src/VpnReconnect.App/App.xaml`
- Create: `src/VpnReconnect.App/MainWindow.xaml`
- Create: `src/VpnReconnect.App/WindowsRecoveryIo.cs`
- Create: `src/VpnReconnect.App/DpapiProtector.cs`
- Create: `src/VpnReconnect.App/LoginTaskRegistrar.cs`
- Modify: `vpn-reconnect.sln`

**Interfaces:**
- Consumes: Task 1 到 Task 7 的全部公开签名。`WindowsRecoveryIo` 实现 `IRecoveryIo`。`DpapiProtector` 实现 `ISecretProtector`。`LoginTaskRegistrar.Apply(LoginTaskSpec spec, bool enabled)`。
- Produces: 可运行的 `VpnReconnect.App`。窗口三页标题为「状态」「设置」「日志」。

- [ ] **Step 1: 写失败测试，锁住窗口文案和拨号约束**

在 `tests/VpnReconnect.Tests/AppContractTests.cs` 读取 `MainWindow.xaml` 文本。

```csharp
[Fact]
public void 主窗口有状态设置日志三页且不包含公网服务器默认值()
{
    var xaml = File.ReadAllText(Path.Combine("src", "VpnReconnect.App", "MainWindow.xaml"));
    Assert.Contains("状态", xaml);
    Assert.Contains("设置", xaml);
    Assert.Contains("日志", xaml);
    Assert.DoesNotContain("101.207.248.238", xaml);
}
```

测试项目把 App 的 xaml 作为额外文件链接，或测试里用仓库根相对路径。运行前先确认该断言失败。

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj --filter FullyQualifiedName~AppContractTests`
Expected: FAIL，文件不存在或缺少三页。

- [ ] **Step 3: 实现 WPF 程序**

`net8.0-windows`，`UseWPF` 为 true。状态页显示是否已连接、隧道 IPv4、目标 ping 说明、最近动作，并有「立即检查」。设置页可改连接名、目标地址、用户名、密码、间隔、开机启动，并有「保存」。密码框不回显已存密码。日志页列为时间、步骤、结果、依据，并有刷新、导出、清空。`--background` 调用 `StartBackground` 且不显示窗口。第二个进程只请求已有进程 `ShowWindow`，不新建检查器。设置页的适配器列表列出当前有 IPv4 的接口名和地址，保存用户选中的接口名。`WindowsRecoveryIo.Dial` 调用 RasDial API 并传入用户名和密码，最多等 30 秒；不启动 `rasdial.exe`。重启使用服务控制管理器重启 RasMan 与 IKEEXT，并用 `pnputil /restart-device` 重启 `SWD\MSRRAS\MS_L2TPMINIPORT`。L2TP 设备未回来时再重启 `MS_NDISWANIP` 和 `MS_NDISWANIPV6`。`DpapiProtector` 使用当前用户 DPAPI。开机开关调用 `LoginTaskRegistrar`，任务参数来自 `LoginTaskSpec`。

- [ ] **Step 4: 运行全部测试**

Run: `dotnet test tests/VpnReconnect.Tests/VpnReconnect.Tests.csproj`
Expected: PASS

- [ ] **Step 5: 不提交，不做发布打包**

手工确认留给用户：断开「公司」后，状态页在一轮检查内变为未连接；保存密码后日志里的拨号依据写明已提交用户名，且导出文件不含密码。不要为了验证去断开正在使用的 VPN，除非用户同意。

## 自检

- 规格里的连通判定、恢复四步、691 停手、三页界面、详细日志、DPAPI、计划任务、关窗与退出，分别落在任务 2、4、7、8。
- 公网地址不会被代码主动 ping，任务 2 有断言。
- 实现者不需要猜测 `RunOnce`、`Judge`、`LoginTaskSpec` 的名字和返回值。
