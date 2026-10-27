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

单元测试 8 个全绿（日志解析 6 + 解决方案解析 2）。

## 路线图

- **M1 已完成** — 项目加载、构建、结构化诊断、配置切换
- **M2 进行中** — 调试：`cdb.exe` / dbgeng 会话，断点、继续、单步、调用栈、局部变量、表达式求值
- **M3** — dump 保存、`!analyze` 崩溃取证
- **M4（可选）** — DTE 通道：在 VS 里打开解决方案、把调试交接给你的 VS 实例旁观

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
