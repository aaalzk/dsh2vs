# dsh2vs

<!-- 收录到 dsh-plugin.org 之后放开下面这行（详情页地址见 docs/marketplace-submission.md）
[![Listed on dsh-plugin.org](https://dsh-plugin.org/badges/listed.svg)](https://dsh-plugin.org/plugins/aaalzk/dsh2vs)
-->

**让你的 AI 助手（DSH 或任何 MCP 客户端）能真正"动手"操作 Visual Studio 的 C++ 工程：加载项目、编译、断点调试。**

不装 VS 插件，不需要 Visual Studio 开着，不会弹出「项目已过期，是否重新生成」这种没人点的对话框。

> An out-of-process MCP bridge that lets an AI agent load, build and debug Visual Studio C++ projects
> without a VSIX, without Visual Studio running, and without modal dialogs blocking automation.

## 作为 DSH 插件安装（一条命令）

```powershell
# 现在就能用（源码安装）
dsh plugin --profile web add github:aaalzk/dsh2vs

# npm 包发布后更短（届时这条才会生效）
dsh plugin --profile web add dsh2vs
```

装完重启会话，`mcp__vs__*` 那 40+ 个工具就直接可用（插件会把 MCP 连接自动接到
`http://127.0.0.1:8790/mcp`）。**桥本身要先跑起来**（见下面「三分钟跑起来」）。

| 项目 | 内容 |
|---|---|
| 能力分类 | 集成与连接（它本质是一座桥：把 DSH 接到一台 Visual Studio 工作站） |
| 一句话价值 | 让 DSH 直接加载、编译、断点调试 VS 的 C++ 工程，不需要装 VSIX、不用 VS 开着、不会被弹窗卡住 |
| 插件形态 | 配置型 bundle：`cordis.patch.yml` 注入 `@deepseek-ai/dsh-mcp-client`，业务在独立的 .NET 桥进程里 |
| 兼容与运行要求 | DSH（任意近期版本）+ **Windows**；需要 .NET 9 SDK、VS 2022 C++ 工具链（MSBuild）、Windows SDK 的 Debugging Tools（cdb.exe） |
| 许可证 | MIT |
| 外部服务 | 只连本机 `127.0.0.1:8790`；无遥测、不上传数据 |

> 如果你已经**手动**在 `~/.dsh/cordis.patch.yml` 里加了 `serverName: vs` 的 MCP 配置，先删掉那一条再装插件，
> 否则会出现两个同名 server。

**TL;DR（三条命令）**

```powershell
git clone https://github.com/aaalzk/dsh2vs.git    # 网络不通见「拿到代码」一节的镜像
cd dsh2vs; dotnet build
.\scripts\start.ps1 -Workspace D:\Projects\你的C++工程
```

跑完打开 <http://127.0.0.1:8790/healthz>，看到 `"status":"ok"` 就成功了。

---

## 这是什么（30 秒版）

- 一个**常驻在后台的小服务**，把「VS 能干的那三件 C++ 的事」变成 HTTP 接口：**加载工程 / 编译 / 调试**。
- AI 助手通过 MCP 协议调用它，就能自己改代码、编译、下断点、看调用栈和变量，不需要你在电脑前点任何按钮。
- 它**不是** VS 插件，**不是**在 VS 里聊天的机器人，也**不替代** VS —— 你想看界面时随时可以自己打开 VS 看。

### 适合谁

| 适合 | 不适合 |
|---|---|
| 让 AI 在无人值守/远程机器上跑 C++ 项目 | 只想在 VS 里跟 AI 聊天（用 Claude Code / cv4vs 那类插件更合适） |
| VS 解决方案 + MSBuild 的 C++ 工程 | 非 C++ 项目（本项目专为 C++ 场景调过） |
| 需要「编译报错 → 改代码 → 再编译」自动循环 | 需要 VS 调试器图形界面（并行栈窗口、可视化查看器） |
| 想摆脱「一弹窗就卡住、还得人工点」的自动化 | 需要调试托管 .NET / 需要混合模式深度集成 |

## 为什么不用现成的 VS MCP 插件

`Unofficial-VS-MCP`、`VS-MCPServer`、`cv4vs-agents` 这些都是**进程内**方案：工具调用最终要回到 VS 的
UI 线程。于是 VS 一弹模态框，调用就阻塞，只能等人点；RDP 断开、锁屏、VS 重启都会让连接挂掉。

| 场景 | 进程内插件 | dsh2vs |
|---|---|---|
| VS 弹「项目已过期」对话框 | 调用卡住，等人点 | 不经过 VS，没有这个弹窗 |
| RDP 断开 / 锁屏 | UI 自动化失效 | 无影响 |
| VS 崩溃或重启 | 连接断开，要人工恢复 | 服务独立，重连即用 |
| 客户端重启 | 要重新拉起 stdio 代理进程 | 固定一个 URL，一直在 |

结论：**C++ 的加载、编译、调试，没有一样必须由 VS 进程提供**。桥就做桥。

## 与现有 MCP 的关系（不重复造轮子）

生态里已经有不少同类，我们不装作没有。这张表说明**边界**，也说明什么时候该用谁：

| 项目 | 它擅长 | 什么时候用它 | 与本项目的关系 |
|---|---|---|---|
| [mcp-windbg](https://github.com/svnscha/mcp-windbg) | 包一层 `cdb`/`kd`：dump 深度分析、用户态远程调试、内核调试、TTD | 已经拿到 dump、要深挖 | **调试原语重叠**，它比我们深 |
| **微软官方 WinDbg MCP**（WinDbg `1.2610.1001.0+` 内置） | 在 WinDbg 界面里用自然语言调试 | 人就坐在 WinDbg 前面 | 需要 GUI 交互，**无人值守用不了** ← 我们的位置 |
| [VS-MCPServer](https://github.com/CodingWithCalvin/VS-MCPServer) | VS 扩展，暴露 solution / document / 编辑类工具 | 想让 AI 操作**开着的** VS | 进程内，VS 必须运行 |
| [VS IDE Bridge](https://github.com/RenegadeRiff86/Visual-Studio-MCP) | VS 扩展 + 后台服务，读写代码、看错误、跑构建 | 同上 | 进程内，要求 VS 2022 17.14+ 运行中 |
| [msbuild-mcp-server](https://github.com/zenoengine/msbuild-mcp-server) | 纯 MSBuild 构建，自动找 MSBuild | 只想编译 | **构建原语重叠**，但没有"编译完接着调试" |
| **dsh2vs** | 从 `.sln` 出发的**无人值守闭环**：解析配置/平台 → 构建 → 找到输出的 exe → 起调试器 → 断点/栈/变量 → 崩溃取证 | 机器前没人、不能弹窗、不能开 VS | 见下 |

三条只有我们做的：

1. **不依赖任何 GUI**。不是"VS 关掉也能连"，而是整条链路都不碰 VS —— 构建走 MSBuild CLI，调试走 cdb 子进程。
2. **工程感知**。别人给你 debugger，你得自己告诉它 exe 在哪；我们从解决方案和配置推出来。
3. **诊断层**：启动失败直接给 `0xC0000135` + 缺哪个 DLL（PE 导入表预检）；卡在弹窗直接判定"CRT 断言，不是死锁"并摘出断言原文；崩溃自动 dump + 报告。

反过来说清楚：**如果你只想要"会跑 cdb 的 MCP"，那别用本项目** —— `mcp-windbg` 更成熟（1700+ star、PyPI 分发、支持内核与 TTD）。本项目把自己定位在"没人能点按钮的那台机器上"。

## 你需要什么

| 依赖 | 用途 | 怎么检查 |
|---|---|---|
| Windows 10/11 | 运行环境 | — |
| Visual Studio 2022 / 2026，勾选「使用 C++ 的桌面开发」 | 提供 `MSBuild.exe`、`cl.exe` | 见下方命令 ① |
| .NET 9 SDK 或更新 | 编译/运行本服务 | `dotnet --list-sdks` |
| Windows SDK 的「Debugging Tools for Windows」 | 提供 `cdb.exe`（调试用） | 见下方命令 ② |

```powershell
# ① 查 VS 是否装了 C++ 工作负载（有输出就 OK）
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath

# ② 查 cdb.exe（True 就 OK）
Test-Path "C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe"
```

`cdb.exe` 没有的话：装 Windows SDK 时勾选 **Debugging Tools for Windows**；已经装了 VS 也可以单独补装。

## 三分钟跑起来

### 第 0 步：拿到代码

三种方式，任选其一：

```powershell
# A. 能直连 GitHub
git clone https://github.com/aaalzk/dsh2vs.git

# B. 直连不通 / 很慢 —— 用镜像加速（把 GitHub 地址原样接在镜像域名后面）
git clone https://gh.223456.xyz/github.com/aaalzk/dsh2vs.git

# C. 不想用 git：浏览器打开仓库 → Code → Download ZIP
```

> 镜像 `https://gh.223456.xyz/` 是**本项目作者搭建的一个加速服务**：它跑的是第三方开源的 GitHub 代理
> 镜像程序（Docker 镜像由该上游项目提供，非本项目自行开发或构建），作者只负责部署和带宽，不对其
> 实现与安全性背书。不方便走第三方时请直连 GitHub 或用 ZIP 下载。
> 已验证支持 `git clone` / `git ls-remote` / raw 原始文件下载；**网页浏览不支持**（会返回 400），
> 看网页请走正常 GitHub。用法就是把 `https://github.com/...` 换成 `https://gh.223456.xyz/github.com/...`。

### 第 1 步：构建

```powershell
cd dsh2vs
dotnet build
```

### 第 2 步：启动（把路径换成你自己的 C++ 工程目录）

```powershell
.\scripts\start.ps1 -Workspace D:\Projects\YourCppRepo
```

> 这个窗口会被服务占住（Ctrl+C 停止）。下一步验证请**另开一个** PowerShell 窗口。

不想用脚本的话，等价写法：

```powershell
dotnet run --project src\Dsh2Vs.Server -- --Dsh2Vs:WorkspaceRoot=D:\Projects\YourCppRepo
```

### 第 3 步：确认活着

```powershell
# PowerShell 里（推荐）
(Invoke-WebRequest http://127.0.0.1:8790/healthz -UseBasicParsing).Content

# 装了 curl.exe 也可以
curl.exe http://127.0.0.1:8790/healthz
```

期望看到类似输出（重点看 `status` 和 `msbuild`）：

```json
{"status":"ok","name":"dsh2vs","version":"0.1.0",
 "workspaceRoot":"D:\\Projects\\YourCppRepo",
 "msbuild":"d:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\MSBuild\\Current\\Bin\\MSBuild.exe"}
```

> `status` 是 `degraded` 说明没找到 MSBuild —— 见「常见问题」第 2 条。

### 第 4 步：接到 DSH

**方式 A（推荐）：装成插件，自动接好**

```powershell
dsh plugin --profile web add github:aaalzk/dsh2vs
```

插件的 `cordis.patch.yml` 会替你做下面这段配置，重启会话即可。

**方式 B：手动改配置**

编辑 `~/.dsh/cordis.patch.yml`，在顶层追加：

```yaml
- insert:
    - id: mcp-dsh2vs
      name: '@deepseek-ai/dsh-mcp-client'
      config:
        serverName: 'vs'
        transport: 'streamable-http'
        url: 'http://127.0.0.1:8790/mcp'
```

**重启 DSH 会话**（MCP 客户端只在会话启动时加载），之后工具会以 `mcp__vs__*` 出现，例如
`mcp__vs__build`、`mcp__vs__debug_launch`。

> 用别的 MCP 客户端（Claude Desktop、Cline、自研 agent）也一样：接 `http://127.0.0.1:8790/mcp`，
> 传输方式选 streamable-http。

## 走一遍完整流程（用自带的 C++ 样例，约 2 分钟）

仓库里带了一个小样例，可以先拿它验证整条链路通不通：

```powershell
# 生成 VS 解决方案（需要 CMake）
cmake -S samples/hello-cpp -B samples/hello-cpp/build -G "Visual Studio 17 2022" -A x64
```

然后让 AI 依次做这几件事（或者你自己按顺序点工具）：

```
project_open  → samples/hello-cpp/build/hello-cpp.sln     # 读到 4 个项目、4 个配置、x64
build                                                      # 编译，返回 succeeded
debug_launch  → samples/hello-cpp/build/Debug/loopy.exe    # 停在初始断点
breakpoint_set→ loopy!main                                 # 下断点
debug_continue                                             # 命中断点，state=stopped
stack_get                                                  # 看到调用栈
locals_get                                                 # 看到 argc / argv / waitMode
debug_stop                                                 # 结束，进程不残留
```

预期结果（本机实测）：

```
stack_get  → loopy!main+0x5 / loopy!invoke_main+0x39 / loopy!__scrt_common_main_seh+0x132 …
locals_get → int argc = 0n1 ; char ** argv = 0x0000018f`55667150 ; bool waitMode = false
```

## 工具清单

**工程与构建**

| 工具 | 作用 |
|---|---|
| `project_open` | 读 `.sln` / `.vcxproj`，返回项目、配置、平台；成为后续默认目标。**不启动 VS。** |
| `project_info` | MSBuild 求值：输出 exe/dll 的 `TargetPath`、`OutDir`、`IntDir`、工程类型 |
| `project_configs` | 列出配置与平台 |
| `project_config_set` | 设定后续默认的 `Configuration` / `Platform` |
| `build` / `rebuild` / `clean` | MSBuild 编译 / 重建 / 清理，返回结构化诊断 |
| `build_errors` | 重读上次构建的错误，不必重新编译 |
| `vs_status` | 服务状态、MSBuild 路径、当前解决方案、上次构建结果 |

**调试（基于 cdb，beta）**

| 工具 | 作用 |
|---|---|
| `debug_launch` / `debug_attach` | 启动 exe（停在初始断点）或附加到 PID。**启动失败会返回 `ok:false` + 退出码 + 缺失依赖清单**，不再是「它退出了」 |
| `debug_preflight` | 启动前检查依赖：读 PE 导入表，逐个在 exe 目录 / System32 / PATH 里解析，直接列缺失的 DLL（毫秒级，不起调试器） |
| `debug_continue` / `debug_step` | 继续 / 单步（into·over·out） |
| `debug_break` | 打断。卡在模态框时 `.break` 不生效，会自动用 `DebugBreakProcess` 注入；注入断点落在一次性线程上，所以它会**自动切到真正卡住的那个线程**，并给出结论式 `diagnosis`（如「CRT 断言，不是死锁」+ 断言原文） |
| `debug_thread_switch` | 切换当前线程（`~Ns`）；之后 `stack_get` / `locals_get` / `evaluate` 都作用于它 |
| `breakpoint_set` / `list` / `remove` | 断点，支持条件表达式；符号没加载时自动降级为 `bu` |
| `exception_set` / `exception_list` | 异常断点：`sxe/sxd/sxn/sxi`，比如 `event=av mode=break` 让程序在第一次访问越界时就停 |
| `stack_get` | 调用栈，解析成 `{frame, returnAddress, symbol, file, line}`；支持 `threadId=` 单次指定线程 |
| `locals_get` | 局部变量（含类型与地址）；支持 `threadId=` |
| `evaluate` | C++ 表达式求值 |
| `threads_get` / `modules_get` / `memory_read` | 线程 / 模块 / 内存 |
| `debug_stop` | 结束会话并杀掉被调试进程（不留孤儿） |
| `debug_execute` | 逃生口：直接发任意 cdb 命令（`.exr -1`、`k`…） |

**崩溃取证（M3）**

| 工具 | 作用 |
|---|---|
| `dump_save` | 给当前被调试进程写完整 minidump（`.dump /ma`），默认存 `%LOCALAPPDATA%\dsh2vs\dumps` |
| `crash_analyze` | 不给参数就分析当前会话；给 `dumpPath` 就在一次性进程里分析 dump。返回异常码、异常名、故障模块、故障帧、失败桶 |
| `debug_open_dump` | 把 .dmp 打开成当前会话，之后 `stack_get` / `locals_get` / `threads_get` 都能对着崩溃现场用 |

> 崩溃分析以**异常记录**（`.exr -1` / `.ecxr`）为准，不依赖系统符号，通常 0.1–1.5 秒返回。
> `!analyze -v` 只有在配置了 `Debug:SymbolServer` 时才会跑 —— 否则它会去联网拉系统符号，
> 卡几分钟还给出 `WRONG_SYMBOLS` 的错误结论（这正是 VS 里常见的那个卡点）。

**VS 联动（M4，需要你的 Visual Studio 正在运行）**

| 工具 | 作用 |
|---|---|
| `vs_dte_status` | 读 VS 当前状态：打开的解决方案、工程数、构建状态、调试器模式 |
| `vs_dte_open` | 在**你的 VS 窗口里**打开某个解决方案（你想看/想改时用） |
| `vs_dte_build` | 用 VS 自己构建当前解决方案，返回 VS 的构建结果 |
| `vs_dte_errors` | 读 VS 的「错误列表」，结构化返回 |
| `vs_dte_attach` | 把进程交给 VS 调试器，方便你旁观。**桥正用 cdb 调试该进程时会拒绝**（一个进程不能被两个调试器挂） |

> 这一层走 DTE（COM），通过 Windows PowerShell 5.1 调 —— 不改你的 VS 状态就没有副作用；
> 但 `vs_dte_open` / `vs_dte_build` 确实会改变 VS 里打开的东西，无人值守时优先用普通的 `build` 工具。

## 给 agent 的操作手册（Skill）

仓库里带了 `skills/dsh2vs/SKILL.md` —— 面向 AI agent 的完整操作流程：编译回路、调试回路、
卡在弹窗怎么办、崩溃取证、VS 联动、工作区规则，以及一张踩过的坑对照表。

装到 DSH：把这个目录放到 `~/.dsh/skills/` 下（或直接让 agent 读文件路径），
下次遇到 C++ 构建/调试任务就会照着流程走，不用每次重新摸索。

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

## 让它常驻（推荐，尤其是远程机器）

```powershell
# 注册两个计划任务：登录自启 + 每 5 分钟探活，挂了自动拉起
.\scripts\install-service.ps1 -Workspace D:\Projects\YourCppRepo

.\scripts\install-service.ps1 -Status      # 看状态
.\scripts\install-service.ps1 -Uninstall   # 卸掉
```

为什么需要这层：Windows 更新、杀软、会话异常都可能把后台进程干掉（作者就遇到过一整批进程
被系统更新连带杀掉）。有了 watchdog，桥自己回来，不用你在电脑前。

## 常见问题

**1. `dotnet` 不是内部或外部命令**
没装 .NET SDK。去 <https://dotnet.microsoft.com/download> 装 .NET 9（或更新）后再开个新终端。

**2. healthz 里 `status` 是 `degraded`，或报 `MSBuild.exe not found`**
说明没找到 VS 的 MSBuild。两种修法：
- VS Installer 里勾上「使用 C++ 的桌面开发」；
- 或者手动指定：`--Dsh2Vs:MsBuildPath="D:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"`。

**3. 调试相关工具报 `cdb.exe not found`**
Windows SDK 里没装 Debugging Tools。装的时候勾「Debugging Tools for Windows」，
或者设环境变量 `DSH2VS_CDB` 指向 `cdb.exe`。

**4. 端口 8790 被占用**
启动时加 `--urls http://127.0.0.1:8800`，同时把 DSH 配置里的 URL 一起改掉。

**5. DSH 里看不到 `mcp__vs__*` 工具**
改完 `cordis.patch.yml` 必须**重启 DSH 会话**；再确认 `curl http://127.0.0.1:8790/healthz` 通。

**6. `debug_launch` 报 `it is outside the workspace`**
出于安全默认只允许调试工作区内的 exe。把 `--Dsh2Vs:WorkspaceRoot` 指到工程根目录，
或（不推荐）在 appsettings 里把 `Debug:AllowTargetsOutsideWorkspace` 设为 `true`。

**7. 断点设不上：`Couldn't resolve error at 'xxx'`**
符号名不对或还没加载。用「模块!函数」形式（如 `loopy!main`），别用匿名命名空间里的裸函数名；
工具会自动降级成延迟断点 `bu`。

**8. `locals_get` 显示的变量是乱值**
断点停在函数入口、序言还没执行时，参数还在寄存器里，cdb 读不到。`debug_step` 几步进入函数体就正常了。

**9. 服务莫名其妙没了 / 第二天发现连不上**
装常驻任务（见上一节）。这类问题基本是系统更新或安全软件连带杀的进程，watchdog 会拉起来。
（作者亲历：一次 Windows 安全平台更新连带杀掉了整批进程，包括桥、远程桌面和 DSH 自己。）

**10. 程序跑着跑着不动了，`debug_continue` 一直返回 `state=running`**
多半是目标弹了个模态框（assert / MessageBox），线程卡在消息循环里。这是**被调试程序**卡住，不是桥卡住。
调用 `debug_break`：它会注入断点、**自动选中真正卡住的线程**并给出 `diagnosis`（弹窗标题 + 断言原文），`stack_get` 直接拿到 `USER32!MessageBoxWorker → 你的函数`，
定位后 `debug_stop` 结束进程即可。

**11. 程序一启动就退出，控制台什么输出都没有**
八成是缺依赖 DLL（`STATUS_DLL_NOT_FOUND` / `0xC0000135`）。现在的返回会直接告诉你：

```json
{"ok": false, "state": "exited", "exitCode": -1073741515, "exitCodeHex": "0xC0000135",
 "diagnosis": "STATUS_DLL_NOT_FOUND: a dependency DLL is missing, the program never started",
 "imports": { "missingCount": 1, "missing": ["spdlogd.dll"] }}
```

想提前查（不必起调试器）：`debug_preflight executable=build\bin\Debug\ZoneServer.exe`。
注意 **Debug 版依赖带 `d` 后缀**（`spdlogd.dll`，不是 `spdlog.dll`）—— 移错文件会得到假的「正常」结果。

**12. GitHub 上不去**
用镜像：`git clone https://gh.223456.xyz/github.com/aaalzk/dsh2vs.git`。

## 已知限制

- 源码行断点在「初始断点」阶段设置可能退化成延迟断点而不生效 —— 建议按符号下断，或第一次停下后再设。
- 函数入口处 `locals_get` 会显示垃圾值（cdb 固有行为，步入函数体后正常）。
- `debug_break` 在目标高速运行时可能延迟返回，随后用 `debug_status` 确认。
- cdb 与 VS 调试器**不能同时**挂同一进程；想自己在 VS 里旁观，先 `debug_stop`。

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

## 安全边界

- 只监听 `127.0.0.1`，不对外暴露。
- 工具参数不能拼任意命令：构建只影响 MSBuild 目标与配置，命令由服务自己构造。
- 调试默认只允许工作区内的可执行文件。

## 目录结构

```
src/Dsh2Vs.Core     公共模型、进程执行、MSBuild 定位
src/Dsh2Vs.Build    MSBuild 调用、日志解析、解决方案解析
src/Dsh2Vs.Debug    cdb 会话、调试会话管理、输出解析
src/Dsh2Vs.Server   MCP 服务（streamable-http）、各工具定义
tests/              单元测试（解析器为主）
samples/hello-cpp   CMake 示例工程（构建 + 调试的验收目标）
scripts/            start.ps1 / install-service.ps1 / watchdog.ps1
```

## 路线图

- ✅ **M1** 项目加载、构建、结构化诊断、配置切换
- ✅ **M2（beta）** cdb 调试：启动/附加、断点、单步、栈、变量、求值、线程、模块、内存
- ✅ **M3** 崩溃取证：`dump_save`、`crash_analyze`、`debug_open_dump`（以异常记录为准，秒级返回）
- ✅ **M4（beta）** VS 联动：`vs_dte_*` —— 读 VS 状态、在 VS 里开解决方案、驱动 VS 构建、读错误列表、调试交接
- ⬜ **M5（可选，已实现可切换）** dbgeng 进程内引擎：`--Dsh2Vs:Debug:Engine=dbgeng` 即可切换。
  去掉子进程与提示符解析，拿结构化控制；代价是引擎崩溃会连带服务，所以默认仍是 cdb（子进程有故障隔离）。
  停止时用引擎自身的 `.kill` 终止目标（`HasExited=true`），但那个已终止的进程对象要等服务退出才从列表消失，属正常现象

## 反馈

开 issue 就行：bug 请附上 `healthz` 输出、VS 版本、以及 `artifacts/` 下的构建日志片段。

## 加速访问 GitHub

本项目作者搭建了一个加速服务：**<https://gh.223456.xyz/>**

服务本身只是**部署**：跑的是第三方开源的 GitHub 代理镜像程序，Docker 镜像来自该上游项目，
非本项目开发或构建，作者只提供部署与带宽，不对其实现与安全性背书。

```powershell
git clone https://gh.223456.xyz/github.com/aaalzk/dsh2vs.git
git clone https://gh.223456.xyz/github.com/<任意仓库>.git
```

支持 `git clone` / `git ls-remote` / raw 文件下载；网页浏览请走原站。

## 许可证

MIT © 2026 aaalzk


