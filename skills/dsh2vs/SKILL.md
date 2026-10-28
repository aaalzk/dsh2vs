---
name: dsh2vs
description: 用 dsh2vs 桥操作 Visual Studio C++ 工程——加载 .sln/.vcxproj、MSBuild 编译、cdb 断点调试、崩溃取证（dump + 异常记录）、以及与正在运行的 Visual Studio 联动。当任务涉及 C++ 项目构建、C++ 程序调试、minidump/崩溃分析、VS 工程结构读取时使用。触发词：编译 C++、VS 工程、断点调试、崩溃分析、dump、Zoneserver、sln、vcxproj、MSBuild。
---

# dsh2vs 操作流程

dsh2vs 是一个常驻 MCP 服务，把「加载工程 / 编译 / 调试 / 崩溃取证 / 联动 VS」暴露成工具。
**不需要 Visual Studio 开着**（只有 `vs_dte_*` 那组需要）。

工具前缀：`mcp__vs__*`（serverName = `vs`）。

## 0. 每次动手前的三秒确认

1. 服务活着：`curl http://127.0.0.1:8790/healthz` → `status: ok`
2. 有工作区：`workspace_list`
3. 工具可见：能看到 `mcp__vs__*`

如果服务没起：`.\scripts\start.ps1 -Workspace <目录>`（前台）或 `Start-ScheduledTask dsh2vs`（已装常驻任务时）。

## 1. 编译回路（最常用）

```
project_open   path=build\Tianyuan4590.sln      # 或 .vcxproj；可用 workspace=<名字>
project_configs                                 # 看有哪些 Debug/Release、x64
project_config_set configuration=Debug platform=x64
build                                           # 或 rebuild / clean
build_errors                                    # 重读上次诊断，不重新编译
```

要点：

- `build` 返回**结构化诊断**（文件/行/列/代码/消息/工程），不用去翻日志；日志路径也会给。
- **不要并发 build**：服务内部已串行化，并发调用只会排队。
- 相对路径按「当前工作区 → 调用时 `workspace=`」解析；找不到文件时会告诉你解析到的绝对路径。

## 2. 调试回路

```
debug_launch  executable=build\bin\Debug\ZoneServer.exe arguments="--config x.json" workspace=zoneserver
breakpoint_set spec=ZoneServer!HandlePacket          # 或 loopy.cpp:42；symbol 未加载会自动降级为 bu
breakpoint_set spec=ZoneServer!HandlePacket condition="playerId > 3"
debug_continue timeoutMs=15000                        # state=running 代表还在跑，不是失败
stack_get frames=16
locals_get                                            # 带类型与地址
evaluate expression="packet->size"
debug_step kind=over|into|out
debug_stop                                            # 结束并杀掉被调试进程，不留孤儿
```

踩过的坑（照着做可以省半小时）：

| 现象 | 原因 | 做法 |
|---|---|---|
| `locals` 全是垃圾值 | 断点停在函数入口、序言还没执行 | `debug_step over` 一两步再看 |
| 源码行断点不生效 | 在「初始断点」阶段设，退化成延迟断点 | 用 `module!function`，或先 `debug_continue` 停一次再设 |
| 断点报 `Couldn't resolve` | 符号名不对（匿名命名空间里的函数名不是裸名字） | `x module!*关键字*` 查真名，或改用源码行 |
| `debug_continue` 长时间 `running` | 目标卡住（多半是弹窗） | 见下一节 |
| attach 被拒 | 目标不在任何工作区内 | `workspace_list`，必要时 `workspace_add` |

## 3. 程序卡住 / 弹窗

`debug_continue` 返回 `state=running` 且长时间没有停点 → 目标很可能卡在模态框（MessageBox / assert）：

```
debug_break
```

它会先用 `.break`，无效就用 `DebugBreakProcess` 从外部注入断点，然后 dump **所有线程**的栈。
输出里能直接看到 `USER32!MessageBoxWorker → USER32!MessageBoxA → 你的函数`，定位后 `debug_stop` 收工。

## 3.5 程序一启动就退出 / 没有输出（缺 DLL）

**判据**：`debug_launch` 返回 `ok:false` + `exitCodeHex: "0xC0000135"`，`diagnosis` 写明
`STATUS_DLL_NOT_FOUND`，`imports.missing` 直接列出缺哪个。

```
debug_preflight executable=build\bin\Debug\ZoneServer.exe
```

毫秒级、不用起调试器：读 PE 导入表，逐个在 exe 目录 / System32 / PATH 里解析。

处理：

1. 看 `missing` 清单，把对应 DLL 从 vcpkg 的 `installed\x64-windows\bin`（Debug 配置是
   `installed\x64-windows\debug\bin`）拷到 exe 同目录。
2. **Debug 版依赖带 `d` 后缀**（`spdlogd.dll` 而不是 `spdlog.dll`）—— 移错文件会得到假的「正常」结果。
3. 拷完再跑一次 `debug_preflight`，`missingCount: 0` 再启动。

其它启动失败的退出码：

| 退出码 | 含义 |
|---|---|
| `0xC0000135` | 缺依赖 DLL（`STATUS_DLL_NOT_FOUND`） |
| `0xC0000142` | 依赖 DLL 初始化失败 |
| `0xC0000409` | /GS、`abort()` 或 `__fastfail` |
| `0x0` | 正常跑完退出 |

> 注意：目标若在**程序自己的** MessageA/assert 里卡住，那不是启动失败 —— 用 `debug_break`（见上）。

## 4. 崩溃取证

**默认已经自动化**：`debug_continue` / `debug_step` 一旦发现目标停在异常上（不是普通断点），会自己
写 minidump、分析异常记录、落一份 markdown 报告，并在返回里带一个 `forensics` 块：

```json
"forensics": {
  "exceptionCode": "0xc0000005", "exceptionName": "access violation",
  "faultingSymbol": "loopy!main+0x16d",
  "dumpPath": "...\\dumps\\loopy-20261028-114950.dmp",
  "reportPath": "...\\reports\\crash-loopy-20261028-114950.md"
}
```

拿到 `reportPath` 就可以直接把报告内容讲给用户，不必再手工 `dump_save` + `crash_analyze`。
需要关掉（省磁盘）：`Dsh2Vs:Debug:AutoForensics=false`。

手工流程仍然可用：

想在**第一时间**停在崩溃点（而不是等 Windows 错误报告）：

```
exception_set event=av mode=break        # 访问越界；eh = C++ 异常；clr = .NET；* = 全部
exception_set event=av mode=second       # 只在第二次机会停
exception_set event=av mode=notify       # 第一次机会只记录不停
exception_list                           # 看当前所有设置
```

崩溃后：

```
crash_analyze                 # 直接分析当前会话：异常码 / 异常名 / 故障帧
dump_save                     # 写 minidump（默认 %LOCALAPPDATA%\dsh2vs\dumps）
crash_analyze dumpPath=<dmp>  # 一次性进程分析 dump，不影响当前会话
debug_open_dump path=<dmp>    # 把 dump 打开成会话，之后 stack_get/locals_get 可用
```

要点：

- 分析走**异常记录**（`.exr` / `.ecxr`），不需要系统符号，通常 0.1–1.5 秒。
- **别指望 `!analyze -v`**：没有符号服务器时它会联网拉符号卡几分钟，还给出 `WRONG_SYMBOLS` 的错结论。
  确实需要时，先配 `Dsh2Vs:Debug:SymbolServer`。
- 读到的 `exceptionCode` 形如 `0xc0000005`（访问越界）、`0xc0000409`（/GS、abort、fastfail）。

## 5. 联动 Visual Studio（人在电脑前时）

```
vs_dte_status                     # VS 打开的是哪个 sln、几个工程、构建状态、调试器模式
vs_dte_open path=<sln>            # 在 VS 里打开（会改变你 VS 当前打开的东西）
vs_dte_build timeoutSeconds=600   # 让 VS 自己构建
vs_dte_errors                     # VS 的「错误列表」
vs_dte_attach processId=<pid>     # 把进程交给 VS 调试器旁观
```

规则：

- `vs_dte_attach` 前必须 `debug_stop` —— 一个进程不能同时被 cdb 和 VS 挂。
- `vs_dte_output` 在当前环境（VS 2022 17.14，外部 DTE 客户端）**读不到输出窗格**，会返回说明；
  需要构建输出就看 `build` 的 `logPath`，需要调试输出就用 `debug_execute`。

## 6. 工作区

```
workspace_list                          # 名字 → 路径，当前是哪个
workspace_add    path=D:\openclaw       # 立即生效并写回 %LOCALAPPDATA%\dsh2vs\workspaces.json
workspace_remove name=openclaw          # 最后一个不能删
workspace_use    name=zoneserver        # 切换默认
```

- 相对路径：调用时 `workspace=` → 当前工作区。
- 安全边界：调试目标必须落在**任一**已配置工作区内，否则拒绝（可用配置放宽）。

## 7. 典型任务怎么串

**「改完代码编译一下」**
`build` → 有错看 `diagnostics` → 改码 → `build`。

**「这个崩溃怎么来的」**
`exception_set av break` → `debug_launch` → `debug_continue` → `crash_analyze` → `stack_get` / `locals_get`
→（需要留存）`dump_save` → `debug_stop`。

**「跑起来就卡住不动」**
`debug_continue` 返回 running → `debug_break` → 看 `USER32!MessageBox*` → `debug_stop`。

**「帮我在 VS 里打开这个工程」**
`vs_dte_open path=<sln>` → `vs_dte_status` 确认。

## 8. 不要做的事

- 不要并发 `build`（服务已串行，抢不到好处）。
- 不要在 cdb 会话还活着时用 `vs_dte_attach`。
- 不要在没有符号服务器时跑 `!analyze -v`。
- 不要把 `debug_launch` 指向工作区外的 exe（会被拒），先 `workspace_add`。
- 不要依赖「函数入口处的局部变量」——先从参数区步入函数体。

## 9. 排障速查

| 症状 | 处置 |
|---|---|
| `status: degraded` / 找不到 MSBuild | 装 VS C++ 工作负载，或配 `Dsh2Vs:MsBuildPath` |
| `cdb.exe not found` | 装 Windows SDK 的 Debugging Tools，或配 `DSH2VS_CDB` |
| 工具列表里没有 `mcp__vs__*` | 改完 DSH 配置要**重启会话** |
| 端口占用 | `--urls http://127.0.0.1:8800`，同时改 DSH 配置 |
| 服务莫名其妙没了 | 装 `scripts/install-service.ps1`（登录自启 + 5 分钟 watchdog） |
