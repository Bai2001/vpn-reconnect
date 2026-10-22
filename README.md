# 公司 VPN 重连

[![CI](https://github.com/Bai2001/vpn-reconnect/actions/workflows/ci.yml/badge.svg)](https://github.com/Bai2001/vpn-reconnect/actions/workflows/ci.yml)
[![Release](https://github.com/Bai2001/vpn-reconnect/actions/workflows/release.yml/badge.svg)](https://github.com/Bai2001/vpn-reconnect/actions/workflows/release.yml)
[![最新版本](https://img.shields.io/github/v/release/Bai2001/vpn-reconnect?label=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC)](https://github.com/Bai2001/vpn-reconnect/releases/latest)

Windows 桌面小工具：登录后常驻后台，按间隔检查公司 L2TP VPN 是否还通；断了自动用软件自己保存的账号密码拨号重连，链路挂死时重启相关服务与网卡。

## 背景

公司 L2TP「公司」连接空闲一段时间会掉，`rasdial` 不带账号密码拨号会空着 `Dial-in User` 拿到 628（端口已断开），看着像密码错，其实是没提交凭据。本程序自己存账号密码，拨号时用 `RasDialW` 提交，并且能区分 628（端口断）和 691（账号密码错）。

## 运行要求

- Windows 10/11，.NET 8 运行时（`net8.0-windows`）
- 需要管理员权限才能重启服务/网卡；开机启动通过计划任务注册（最高权限）

## 下载

到 [Releases](https://github.com/Bai2001/vpn-reconnect/releases/latest) 下载 `VpnReconnect-<版本>-win-x64.zip`：
里面是**单个自包含 exe**（自带 .NET 8 运行时，免安装），解压双击即可运行。

## 构建与运行

```powershell
dotnet build vpn-reconnect.sln
dotnet test vpn-reconnect.sln

# 运行（Debug 产物）
.\src\VpnReconnect.App\bin\Debug\net8.0-windows\VpnReconnect.exe
```

带 `--background` 启动时不弹窗口，只驻留托盘（开机启动的计划任务用的就是这个）。
Windows 重启/更新后自动重开本程序时也会带上该参数，因此同样不会弹窗；只有用户主动打开程序（开始菜单、桌面快捷方式、双击 exe）才会显示窗口。

## 使用

1. 打开程序，在「设置」→「VPN 连接」里**选中正在连着的公司适配器**（如「公司 192.168.4.28」）。
   - 选中的名字就是拨号用的 VPN 名，同时用来判断是否拿到了地址。
   - 断线时该适配器会从列表消失，所以请在能连上时选一次并保存。
2. 填用户名、密码（密码用当前 Windows 账号的 DPAPI 加密保存，不回显已存密码），可选填目标地址（如 `192.168.4.100`，填了以 ping 通为准）。
3. 设检查间隔（15–600 秒）和是否开机启动，点「保存」。
4. 关掉窗口程序不退出，会缩到托盘继续检查；托盘图标颜色即连接状态，右键可打开窗口/立即检查/退出。

### 开机启动与管理员权限

「开机启动」有两种落地方式，保存时自动选：

| 方式 | 需要管理员 | 能力 |
| --- | --- | --- |
| 计划任务（`/RL HIGHEST`） | 是 | 断线时能重启 RasMan/IKEEXT/网卡，完整恢复 |
| 注册表 `HKCU\...\Run` | 否 | 只能拨号；重启服务会被拒绝 |

普通权限下保存会自动退回注册表自启，并在设置页说明。调试构建（`bin\Debug`）保存时**不会**注册开机启动，
并会顺手清掉指向该调试 exe 的旧自启动，避免开发调试污染系统自启。想要完整恢复能力（推荐），
**右键 exe →「以管理员身份运行」→ 进设置点一次「保存」**，就会改注册为高权限计划任务
（同时清掉注册表自启，避免登录时启动两份）。设置页下方会显示「当前权限 + 当前自启动方式」。

## 自动更新

装过一次之后就不用再手动下载了：

- 启动时查一次最新版本，之后每 24 小时查一次（GitHub Releases API，匿名调用够用）。
- 发现新版本时：状态页右上角出现「更新到 vX.Y.Z」按钮，托盘也会弹提示；右键托盘「检查更新」也能手动查。
- 点按钮会二次确认，确认后自动 **下载 → 校验 sha256 → 替换 exe → 重启**（替换前把旧文件改名成 `.old`，
  下次启动自动清理；重启带 `--takeover`，先放开单实例锁再拉新进程，不会出现更新完程序不见了）。
- 想彻底关掉：设置页取消「自动更新」并保存。
- 更新只替换 `VpnReconnect.exe` 自身，`%LOCALAPPDATA%\VpnReconnect\` 里的设置、凭据、日志都不动；
  计划任务指向的还是同一个路径，不用重新注册。
- 拿不到安装包或校验和、或者没有写权限时不会硬装，只提示去 Release 页面手动下载。

## 恢复策略

1. 健康不做任何多余动作。
2. 不通先拨一次。
3. 30 秒仍不通或链路挂死：结束残留拨号 → 重启 `RasMan`、`IKEEXT` → 重启 `MS_L2TPMINIPORT`（没回来再重启 `MS_NDISWANIP`/`MS_NDISWANIPV6`）→ 再拨一次。
4. 遇到 691（账号密码错）停止自动重拨，直到保存新密码，避免把服务打进循环。

## 数据文件

都在 `%LOCALAPPDATA%\VpnReconnect\`：

| 文件 | 说明 |
| --- | --- |
| `settings.json` | 界面设置，不含密码 |
| `credentials.bin` | 用户名与密码（当前用户 DPAPI 加密） |
| `connection.log` | JSON Lines 日志，保留 7 天或 5 MB |
| `update\` | 自动更新的下载包与临时 exe，装完一天后自动清理 |

日志列：时间 / 步骤 / 结果 / 依据，界面倒序显示最近 500 条，更早的用「导出」查看。**日志不含密码。**

状态不变时「检查」行每 5 分钟才记一条，状态变化立即记录，拨号与重启过程全部记录。

## 发布新版本

发布是自动化的（`.github/workflows/release.yml`）：

```powershell
git tag v1.2.0
git push origin v1.2.0
```

推上去之后 GitHub Actions 会编译 → 跑单元测试 → 发布自包含单文件 exe → 打包 zip 与 sha256 → 建 Release 并附上产物。
Release 建好后，已经装了旧版本的机器最迟 24 小时内就会收到更新提示（资产名必须是
`VpnReconnect-<版本>-win-x64.zip` 加同名 `.sha256`，工作流已经是这个格式）。
也可以在 Actions 页面手动运行「Release」工作流，填 `1.2.0` 这样的版本号（会以当前 main 提交为基准建 tag）。

`push` 到 `main` / 提 PR 会触发 `ci.yml`，只做编译与单元测试。

## 目录结构

```
src/VpnReconnect.Core    健康判定、恢复流程、日志、凭据、版本比较（无 UI 依赖）
src/VpnReconnect.App     WPF 外壳：三页界面、托盘、单实例、开机启动、自动更新
tests/VpnReconnect.Tests 单元测试
tools/DialOnce           手动拨号一次的命令行工具（排查用）
tools/make-icon.ps1      生成应用图标与托盘状态图标
docs/superpowers/        设计规格与实施计划
```

## 安全

- 密码只以 DPAPI 密文落盘，不写进日志、导出文件或命令行。
- 日志导出前会把出现过的密码替换成 `***`。
