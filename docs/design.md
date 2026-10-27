# dsh2vs 设计说明

## 1. 目标与非目标

**目标**：让 DSH 这类外部 agent 在无人值守的情况下，对 Visual Studio C++ 工程完成
「加载项目 → 编译 → 调试」，且不会因为 GUI 弹窗、锁屏、误操作而断连。

**非目标**：
- 不在 VS 里嵌入聊天面板（那是 `dliedke/ClaudeCodeExtension`、`cv4vs-agents` 的形态）。
- 不替代 VS 给人看的界面；VS 仍然可以打开看，但不是运行链路的一部分。
- M1 不做编辑能力（改文件由 agent 自己的文件工具完成，更可靠）。

## 2. 关键决策

### ADR-001：独立 HTTP 服务，而不是 VSIX

进程内 MCP 的根本缺陷不是工具数量，而是**执行位置**：所有调用回到 VS 的 UI 线程，
任何模态对话框（"项目已过期"、"文件已被外部修改"、附加调试器安全警告）都会让调用挂住，
直到有人点掉。远程无人值守时这就是死锁。

独立服务把执行位置移到普通进程：没有 UI 线程、没有对话框、没有焦点依赖。
代价是放弃"VS 已经为你加载好的状态"，所以项目模型必须自己解析（见 ADR-003）。

### ADR-002：streamable-http + 无状态

- **stdin/stdout 传输**需要一个常驻代理进程与客户端做生命周期配对，客户端重启就要重拉，
  且无法从外部探活（`vs-mcp` 的 `StdioProxy.exe` 就是这种）。
- **streamable-http** 让端点成为稳定契约：`curl /healthz` 可探活，客户端断线重连不需要任何
  协同，多个客户端（DSH 会话、脚本）可同时连。
- **无状态**（不签发 `Mcp-Session-Id`）：服务重启后旧会话 ID 失效会直接 404，这本身就是
  "必须人肉恢复"的一种；无状态把这类故障从设计上消除。

### ADR-003：项目模型用 MSBuild 求值，不用 DTE

`DTE.Solution` / `DTE.SolutionBuild.Build()` 要求 VS 已经打开该解决方案，并且会触发 VS 的
构建 UI 行为。MSBuild 是 VS 自己用的构建引擎，同样的 `.sln` / `.vcxproj`、同样的属性求值：

- `-getProperty:TargetName;TargetPath;OutDir;IntDir;ConfigurationType` → 输出二进制路径与工程类型
- `-flp:logfile=...;verbosity=normal` → 结构化日志，供解析诊断

因此 `.sln` 的解析只做**结构读取**（项目列表、SolutionConfigurationPlatforms），
语义部分全部交给 MSBuild，避免自己实现一套与 VS 不一致的项目系统。

### ADR-004：调试走 cdb / dbgeng，而不是 VS 调试器（M2）

- VS 调试器只能通过 DTE 的 `Debugger` 对象控制，能力受限于 DTE（表达式求值弱、
  并行栈/内存/寄存器基本没有），且必须 VS 在前台会话里。
- `cdb.exe` / `dbgeng.dll` 就是 VS 底层使用的同一套调试引擎，纯命令行，支持断点（源码/符号/地址/
  条件）、调用栈、局部变量、`dx` 表达式、内存读写、模块、`!analyze`。
- 它与"人不在电脑前"天然兼容：RDP 断开、锁屏都不影响。

**互斥限制**：同一进程不能同时被 cdb 和 VS 调试器挂接。需要"你旁观"时做交接
（先停 cdb 会话，再由 VS attach）。

## 3. 进程与数据流

```
DSH ──HTTP/JSON-RPC──▶ Dsh2Vs.Server (127.0.0.1:8790)
                          │
                          ├─ Dsh2Vs.Build ──▶ MSBuild.exe ──▶ cl.exe / link.exe
                          │                     └─ .log 文件 ──▶ 诊断解析
                          │
                          └─ (M2) Dsh2Vs.Debug ──▶ cdb.exe / dbgeng ──▶ 目标进程
```

会话状态（当前解决方案、Configuration/Platform、上次构建结果）保存在服务内存中，
随服务重启清空——这是有意的：状态可重建，不需要持久化带来的失效问题。

## 4. 工具契约

所有工具返回 JSON 文本（`content[0].text`），字段稳定，便于模型解析。

### project_open

```jsonc
// in
{ "path": "samples/hello-cpp/build/hello-cpp.sln" }
// out
{ "opened": true, "kind": "solution", "solution": "D:\\...\\hello-cpp.sln",
  "configurations": ["Debug","Release","MinSizeRel","RelWithDebInfo"],
  "platforms": ["x64"], "projectCount": 4,
  "projects": [{ "Name": "hello", "FullPath": "D:\\...\\hello.vcxproj" }] }
```

相对路径以 `Dsh2Vs:WorkspaceRoot` 为基准。

### build / rebuild / clean

```jsonc
// in（全部可选，缺省用会话状态）
{ "path": null, "configuration": "Debug", "platform": "x64" }
// out
{ "success": false, "exitCode": 1, "verb": "Build", "configuration": "Debug", "platform": "x64",
  "durationMs": 1519, "errorCount": 2, "warningCount": 0, "truncated": false,
  "summary": "2 error(s), 0 warning(s), exit code 1",
  "diagnostics": [{ "Severity": "error", "Code": "C2039", "File": "D:\\src\\main.cpp",
                    "Line": 9, "Column": 34, "Message": "...", "Project": "...hello.vcxproj" }],
  "logPath": "C:\\Users\\...\\Temp\\dsh2vs\\logs\\....log" }
```

诊断去重（同一错误在日志与 stdout 各出现一次）、去掉并行构建的 `6>` 节点前缀、
上限 200 条（`truncated` 标记）。

### project_info

```jsonc
{ "project": "...hello.vcxproj", "configuration": "Debug", "platform": "x64",
  "targetName": "hello", "targetPath": "D:\\...\\Debug\\hello.exe",
  "outputDirectory": "D:\\...\\Debug\\", "intermediateDirectory": "hello.dir\\Debug\\",
  "configurationType": "Application",
  "configurations": [...], "platforms": ["x64"] }
```

## 5. 并发与超时

- 构建串行化（`SemaphoreSlim`）：两个 MSBuild 同时写同一棵 obj 树会互相破坏。
- 单次构建超时 `BuildTimeoutSeconds`（默认 1800），超时杀进程树并返回失败。
- 日志落 `%TEMP%\dsh2vs\logs`，便于事后复核（不依赖客户端还在线）。

## 6. 安全边界

- 仅绑定回环地址。
- 工具不接受任意命令行：参数被限定为路径、Configuration、Platform，命令与参数由服务构造。
- M2 启动目标进程时，默认限定在工作区内的可执行文件路径（待实现开关）。

## 7. 已知限制

- MSBuild 的 `-getProperty` 需要 17.8+（VS 2022 17.8 / VS 2026 均满足）；更旧版本会降级为 XML 解析。
- CMake 工程需要先 `cmake -G "Visual Studio 17 2022"` 生成 `.sln`；服务不替你跑 configure（后续可加）。
- 调试（M2）与 VS 调试器互斥，同时挂接同一进程会失败。

## 8. 里程碑

| 阶段 | 范围 | 状态 |
|---|---|---|
| M0 | HTTP MCP 骨架、`/healthz` | ✅ |
| M1 | 项目加载、构建、结构化诊断、配置切换 | ✅ 已实测 |
| M2 | cdb/dbgeng 调试会话：启动/附加/断点/单步/栈/局部变量/求值 | 进行中 |
| M3 | dump、`!analyze` 崩溃取证 | 待办 |
| M4 | 可选 DTE：在 VS 中打开解决方案、调试交接 | 待办 |
