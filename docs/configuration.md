# 依赖、配置与工作区

> 上一篇：[README](../README.md) · 相关：[工具清单](tools.md) · [常见问题](faq.md)

## 你需要什么

| 依赖 | 用途 | 怎么检查 |
|---|---|---|
| Windows 10/11 | 运行环境 | — |
| Visual Studio 2022 / 2026，勾选「使用 C++ 的桌面开发」 | 提供 `MSBuild.exe`、`cl.exe` | 见下方命令 ① |
| Windows SDK 的「Debugging Tools for Windows」 | 提供 `cdb.exe`（调试用） | 见下方命令 ② |
| .NET 9 SDK | **仅源码构建需要**（用绿色版则不需要） | `dotnet --list-sdks` |

```powershell
# ① 查 VS 是否装了 C++ 工作负载（有输出就 OK）
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath

# ② 查 cdb.exe（True 就 OK）
Test-Path "C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe"
```

`cdb.exe` 没有的话：装 Windows SDK 时勾选 **Debugging Tools for Windows**；已经装了 VS 也可以单独补装。


---

## 配置项

`src/Dsh2Vs.Server/appsettings.json`，也可以用命令行覆盖：

```powershell
dotnet run --project src\Dsh2Vs.Server -- `
  --Dsh2Vs:WorkspaceRoot=D:\Projects\X `
  --Dsh2Vs:MsBuildPath="...\MSBuild.exe" `
  --Dsh2Vs:Debug:AllowTargetsOutsideWorkspace=false `
  --urls http://127.0.0.1:8790
```

| 配置 | 默认 | 说明 |
|---|---|---|
| `Dsh2Vs:WorkspaceRoot` | 当前目录 | 相对路径基准，也是调试目标的安全边界 |
| `Dsh2Vs:MsBuildPath` | 自动探测 | 也可用环境变量 `DSH2VS_MSBUILD` |
| `Dsh2Vs:BuildTimeoutSeconds` | 1800 | 单次构建超时 |
| `Dsh2Vs:Debug:IdleTimeoutMinutes` | 20 | 调试会话空闲多久自动回收 |
| `Dsh2Vs:Debug:AllowTargetsOutsideWorkspace` | false | 是否允许调试工作区外的 exe |
| `Dsh2Vs:Debug:CommandTimeoutSeconds` | 30 | 单条调试命令默认超时 |
| `Dsh2Vs:Workspaces` | 空 | 额外的命名工作区数组，`[{ "name": "...", "path": "..." }]` |
| `Dsh2Vs:WorkspacesFile` | `%LOCALAPPDATA%\dsh2vs\workspaces.json` | 工作区配置文件路径 |
| `Dsh2Vs:Debug:SymbolServer` | 空 | 符号服务器链（如 `srv*C:\symbols*https://msdl.microsoft.com/download/symbols`）。**默认留空**：一旦配置，`!analyze -v` 才会联网拉系统符号，会明显变慢 |
| `Dsh2Vs:Debug:Engine` | `cdb` | 调试后端：`cdb`（子进程，故障隔离）或 `dbgeng`（进程内，少一次进程跳转） |
| `Dsh2Vs:Debug:AutoForensics` | `true` | 目标停在异常上时自动 `dump` + 分析 + 写报告（`%LOCALAPPDATA%\dsh2vs\reports`） |
| `Dsh2Vs:Debug:ForensicsDirectory` | 空 | 崩溃报告目录，默认同上 |


---

## 多工作区（一个服务管多个仓库）

服务不必只认一个目录。工作区是**配置项**，默认从
`%LOCALAPPDATA%\dsh2vs\workspaces.json` 读（改完重启服务即可，不用重装任务）：

```json
[
  { "name": "dsh2vs",     "path": "D:\\Projects\\dsh2vs" },
  { "name": "zoneserver", "path": "D:\\Projects\\Tianyuan4590" }
]
```

也可以写在 appsettings 里（`Dsh2Vs:Workspaces` 数组），或用命令行
`--Dsh2Vs:WorkspacesFile=<路径>` 指到别处。原来的 `--Dsh2Vs:WorkspaceRoot` 仍然有效，
会被当成名为 `default` 的工作区，向后兼容。

用法：

```
workspace_list                         # 看有哪些根、当前是哪个
workspace_add   path=D:\openclaw       # 加一个（立即生效并写回文件，不用重启、不用找配置文件）
workspace_remove name=openclaw         # 删一个（最后一个不能删）
workspace_use  name=zoneserver         # 切换默认工作区
project_open   path=build\Tianyuan4590.sln workspace=zoneserver   # 单次指定
debug_launch   executable=build\bin\Debug\ZoneServer.exe workspace=zoneserver
```

两条规则：

- **相对路径**按「指定/当前工作区」解析；
- **安全边界**放宽为「任意一个已配置工作区内」，仍然拒绝工作区外的目标（除非显式配置允许）。


---

## 让它常驻（推荐，尤其是远程机器）

```powershell
# 注册两个计划任务：登录自启 + 每 5 分钟探活，挂了自动拉起
.\scripts\install-service.ps1 -Workspace D:\Projects\YourCppRepo

.\scripts\install-service.ps1 -Status      # 看状态
.\scripts\install-service.ps1 -Uninstall   # 卸掉
```

为什么需要这层：Windows 更新、杀软、会话异常都可能把后台进程干掉（作者就遇到过一整批进程
被系统更新连带杀掉）。有了 watchdog，桥自己回来，不用你在电脑前。


---

## 安全边界

- 只监听 `127.0.0.1`，不对外暴露。
- 工具参数不能拼任意命令：构建只影响 MSBuild 目标与配置，命令由服务自己构造。
- 调试默认只允许工作区内的可执行文件。
