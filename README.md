# dsh2vs

**给 DSH（以及任何 MCP 客户端）用的 Visual Studio C++ 桥。**

一个常驻的 MCP 服务，把「加载项目 / 编译 / 调试」这三件 C++ 开发里真正需要 Visual Studio 的事，
暴露成稳定的 HTTP 端点。**不装 VSIX、不需要 VS 进程活着、没有模态框、不会因为误点鼠标断连。**

> An always-on MCP bridge for Visual Studio C++ workflows (load solution → build → debug),
> designed for unattended remote agents such as [DeepSeek Harness](https://github.com/deepseek-ai).

---

## 为什么不做一个 VS 插件

现有的 VS MCP 插件（`Unofficial-VS-MCP`、`VS-MCPServer`、`cv4vs-agents`…）都是**进程内**方案：
工具调用最终回到 VS 的 UI 线程。于是——

| 现象 | 进程内插件 | dsh2vs |
|---|---|---|
| VS 弹「项目已过期，是否重新生成」 | 调用阻塞，只能等人点 | 不经过 VS，无此弹窗 |
| RDP 断开 / 锁屏 | UI 自动化失效 | 无影响 |
| VS 崩溃或重启 | 连接断开，要人肉恢复 | 服务不受影响，重连即用 |
| 客户端重启 | 要重新拉起 stdio 代理进程 | 一个 URL，永远在 |
| 误操作 | 可能把管道/扩展搞挂 | 最坏重启服务，一条命令 |

结论：**C++ 的加载/编译/调试没有一样必须由 VS 进程提供**。桥就做桥，别把 agent 塞进 IDE。

## 快速开始

```powershell
git clone https://github.com/aaalzk/dsh2vs.git
cd dsh2vs
dotnet build

# 启动（把 Workspace 指向你的代码根目录）
.\scripts\start.ps1 -Workspace D:\Projects\YourRepo
```

或手动：

```powershell
dotnet run --project src\Dsh2Vs.Server -- --Dsh2Vs:WorkspaceRoot=D:\Projects\YourRepo
```

确认存活：

```powershell
curl http://127.0.0.1:8790/healthz
# {"status":"ok","msbuild":"d:\\Program Files\\...\\MSBuild.exe", ...}
```

MCP 端点在 `http://127.0.0.1:8790/mcp`（streamable-http，无状态，可随时重连）。

## 接到 DSH

`~/.dsh/cordis.patch.yml` 顶层追加：

```yaml
- insert:
    - id: mcp-dsh2vs
      name: '@deepseek-ai/dsh-mcp-client'
      config:
        serverName: 'vs'
        transport: 'streamable-http'
        url: 'http://127.0.0.1:8790/mcp'
```

重启 DSH 会话后，工具以 `mcp__vs__*` 出现。

## 工具

| 工具 | 作用 |
|---|---|
| `project_open` | 读 `.sln` / `.vcxproj`，返回项目列表、配置、平台；成为后续调用默认目标。**不启动 VS。** |
| `project_info` | MSBuild 求值：`TargetName` / `TargetPath`（输出 exe 或 dll 的绝对路径）/ `OutDir` / `IntDir` / `ConfigurationType` |
| `project_configs` | 列出配置与平台 |
| `project_config_set` | 设定后续调用默认的 `Configuration` / `Platform` |
| `build` | MSBuild 编译，返回 `success`、耗时、日志路径、**结构化错误**（文件/行/列/代码/消息/项目） |
| `rebuild` | `-t:Rebuild` |
| `clean` | `-t:Clean` |
| `build_errors` | 重读上次构建的诊断，不必重新编译 |
| `vs_status` | 服务状态、解析到的 MSBuild、当前解决方案、上次构建结果 |

调试（M2，基于 cdb）：

| 工具 | 作用 |
|---|---|
| `debug_launch` | 以 cdb 启动 exe，停在初始断点，便于先下断点再跑 |
| `debug_attach` | 按 PID 附加（目标映像需在工作区内） |
| `debug_continue` | `g`，等到断点/异常/退出，或超时返回 `state=running` |
| `debug_step` | `into` / `over` / `out` 单步 |
| `debug_break` | `.break` 打断正在运行的目标 |
| `breakpoint_set` | `bp`（解析失败自动降级为延迟断点 `bu`），支持条件表达式 |
| `breakpoint_list` / `breakpoint_remove` | `bl` / `bc` |
| `stack_get` | 调用栈，解析成 `{frame, returnAddress, symbol, file, line}` |
| `locals_get` | `dv /t /v`，带类型与地址的局部变量 |
| `evaluate` | `??` C++ 表达式求值 |
| `threads_get` / `modules_get` / `memory_read` | `~` / `lm` / `db` |
| `debug_stop` | 结束会话并杀掉被调试进程（不留孤儿） |
| `debug_execute` | 逃生口：任意 cdb 命令（`!analyze -v`、`.exr -1`、`k`…） |

## 实测（M1）

`samples/hello-cpp`（CMake → VS 2022 解决方案）：

```
project_open  → 4 projects (ALL_BUILD/ZERO_CHECK/hello/math_util), 4 configs, x64
build         → success, 6883 ms, 0 error / 0 warning, hello.exe
project_info  → TargetPath=...\Debug\hello.exe, ConfigurationType=Application
故意写坏一行的 build → 2 error(C2039, C3861), 精确到 main.cpp:9:34, 去重后返回
rebuild       → success, 4801 ms
运行 hello.exe → add(7, 6) = 13 / multiply(7, 6) = 42 / divide(3.0, 4.0) = 0.750
```

单元测试 15 个全绿（日志解析 6 + 解决方案解析 2 + cdb 输出解析 7）。

## 实测（M2 调试，`loopy.exe`）

```
debug_launch   → 停在初始断点，state=stopped
breakpoint_set → loopy!main
debug_continue → Breakpoint 0 hit，state=stopped
debug_step     → over 生效，逐行推进
stack_get      → 5 帧：loopy!main+0x5 / invoke_main+0x39 / __scrt_common_main_seh+0x132 …
locals_get     → int argc = 0n1 ; char ** argv = 0x0000018f`55667150 ; bool waitMode = false
evaluate       → argc → int 0n1
breakpoint_list→ " 0 e 00007ff6`54061540 0001 (0001) 0:**** loopy!main"
debug_stop     → 会话关闭，cdb/loopy 残留进程数 = 0
```

## 路线图

- **M1 完成** — 项目加载、构建、结构化诊断、配置切换
- **M2 完成（beta）** — cdb 调试会话：启动/附加、断点、单步、栈、局部变量、求值、线程、模块、内存、停止
- **M3 待做** — dump 保存、`!analyze` 崩溃取证
- **M4 待做** — DTE 通道：在 VS 里打开解决方案、调试交接给你的 VS 实例旁观

## 常驻与自愈

```powershell
# 注册「登录自启 + 每 5 分钟探活重启」两个计划任务
.\scripts\install-service.ps1 -Workspace D:\Projects\YourRepo

.\scripts\install-service.ps1 -Status      # 查看状态
.\scripts\install-service.ps1 -Uninstall   # 移除
```

`watchdog.ps1` 探 `/healthz`，不通就重新拉起服务并写 `artifacts/watchdog.log`。
这层是专门为「Windows 更新 / 会话异常 / 误点导致进程消失」准备的：桥挂了会自己回来。

## 已知限制

- 目标程序参数**以 `-` 开头时可能被 cdb 当成自己的选项吃掉**（`--` 在 cdb 里是选项组合，不是分隔符）。普通路径类参数正常。
- 源码行断点若在「初始断点」处设置，会退化成延迟断点且可能不生效；建议按符号下断（`module!func`），或在第一次停下后再设。
- 函数入口（序言尚未执行）时 `locals_get` 会读到垃圾值 —— cdb 的固有行为，步入函数体后正常。
- `debug_break` 在目标高速运行时可能延后才返回；随后用 `debug_status` 确认状态更稳。
- cdb 调试器与 VS 调试器不能同时挂同一进程，需要「你旁观」时先 `debug_stop` 再在 VS 里附加。

## 环境要求

- Windows 10/11
- Visual Studio 2022 或 2026，含 **C++ 工作负载**（提供 `MSBuild.exe` 与 `cl.exe`）
- .NET 9 SDK 或更高（`dotnet --list-sdks`）
- 调试能力（M2）额外需要 **Windows SDK 的 Debugging Tools**：`cdb.exe`

MSBuild 解析顺序：`Dsh2Vs:MsBuildPath` 配置 → `DSH2VS_MSBUILD` 环境变量 → `vswhere` → 常见安装路径。

## 配置

`src/Dsh2Vs.Server/appsettings.json`，或命令行覆盖：

```powershell
dotnet run --project src\Dsh2Vs.Server -- `
  --Dsh2Vs:WorkspaceRoot=D:\Projects\X `
  --Dsh2Vs:MsBuildPath="D:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  --urls http://127.0.0.1:8790
```

## 安全

- 只监听 `127.0.0.1`，不对外网暴露。
- 不执行任意命令：所有工具参数只影响 MSBuild 目标与配置。
- 调试会话（M2）会启动进程；默认只允许工作区内的可执行文件路径。

## 许可证

MIT © 2026 aaalzk
