# dsh2vs

**[中文](README.md) · [English](README_EN.md)**

[![npm version](https://img.shields.io/npm/v/dsh2vs.svg)](https://www.npmjs.com/package/dsh2vs)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**让你的 AI 助手（DSH 或任何 MCP 客户端）真正"动手"操作 Visual Studio 的 C++ 工程：加载项目、编译、断点调试、崩溃取证。**

不装 VS 插件，不需要 Visual Studio 开着，不会弹出「项目已过期，是否重新生成」这种没人点的对话框。

> ### ⚠️ 非官方、独立第三方项目
> dsh2vs 是独立开发者 [aaalzk](https://github.com/aaalzk) 的个人项目，**与 DeepSeek AI、Microsoft 以及
> Model Context Protocol 项目均无隶属关系，也未获其认可或背书**。
> 名字里的 "dsh" 指 DeepSeek Harness（它对接的工具）、"vs" 指 Visual Studio（它驱动的对象）——
> 两者都只是**被本项目从外部调用的第三方产品**。本项目不发行、不代表、不打包其中任何一个。

---

## 安装：两步，先起桥，再接 DSH

dsh2vs 由**两个独立部分**组成，只装一半会连上一个不存在的地址：

| 部分 | 是什么 | 怎么装 |
|---|---|---|
| **① 桥** | 一个 .NET 程序，真正调 MSBuild 编译、起 cdb 调试、分析崩溃 | 下载绿色版解压即用，或自己 `dotnet build` |
| **② DSH 插件** | 一个纯配置包，只负责告诉 DSH「桥在 127.0.0.1:8790」 | `dsh plugin --profile web add dsh2vs` |

```
DSH  ──MCP──▶  ② 插件（6 行 YAML：告诉 DSH 打哪个号码）
                    │
                    ▼  HTTP
                ① 桥（真正干活儿的 .NET 程序）
                    ├─▶ MSBuild.exe      编译
                    ├─▶ cdb.exe          调试
                    └─▶ 崩溃 dump + 报告
```

### 第 1 步：起桥

**A. 绿色版（推荐：不用装 .NET SDK、不用 git、不用编译）**

去 [Releases](https://github.com/aaalzk/dsh2vs/releases) 下载 `dsh2vs-win-x64.zip`，解压后运行：

```powershell
.\Dsh2Vs.Server.exe --Dsh2Vs:WorkspaceRoot=D:\Projects\你的C++工程
```

**B. 源码构建（要改代码才需要）**

```powershell
git clone https://github.com/aaalzk/dsh2vs.git
cd dsh2vs; dotnet build
.\scripts\start.ps1 -Workspace D:\Projects\你的C++工程
```

**C. MCPB 单文件包（给 Claude Desktop 等支持 MCPB 的客户端）**

从 [Releases](https://github.com/aaalzk/dsh2vs/releases) 下载 `dsh2vs-0.1.0.mcpb`（自带 .NET 9 运行时，约 43 MB），
直接用 Claude Desktop 打开即可；或解压后手动以 stdio 模式启动：

```powershell
.\Dsh2Vs.Server.exe --stdio
```

stdio 模式下 stdout 只走 MCP 协议，日志全部写 stderr，不会污染通信。

### 第 2 步：接到 DSH

```powershell
dsh plugin --profile web add dsh2vs
```

（npm 不通时用源码安装：`dsh plugin --profile web add github:aaalzk/dsh2vs`）

**重启 DSH 会话**（MCP 连接只在会话启动时加载），`mcp__vs__*` 那 40+ 个工具就会出现。

> ⚠️ 只装插件、桥没起 → DSH 会**明确报错**（`failOnStartupError: true`），不会静默丢工具。
> 顺序一定是：先确认 `healthz` 返回 `ok`，再装插件。

### 确认桥活着

```powershell
(Invoke-WebRequest http://127.0.0.1:8790/healthz -UseBasicParsing).Content
```

```json
{"status":"ok","name":"dsh2vs","version":"0.1.0","msbuild":"...MSBuild.exe"}
```

> `status` 是 `degraded` 说明没找到 MSBuild —— 见[常见问题](docs/faq.md)。

---

## 这是什么（30 秒版）

- 一个**常驻后台的小服务**，把「VS 能干的那三件 C++ 的事」变成 HTTP 接口：**加载工程 / 编译 / 调试**。
- AI 助手通过 MCP 调用它，就能自己改代码、编译、下断点、看调用栈和变量，不需要你在电脑前点按钮。
- 它**不是** VS 插件、**不是**在 VS 里聊天的机器人、也**不替代** VS —— 你想看界面时随时自己打开 VS。

**适合谁**：让 AI 在无人值守/远程机器上跑 C++ 项目；需要「编译报错 → 改代码 → 再编译」自动循环；想摆脱「一弹窗就卡住、还得人工点」。

**不适合谁**：只想在 VS 里跟 AI 聊天（用 Claude Code / cv4vs 那类插件更合适）；需要 VS 调试器图形界面；需要调试托管 .NET。

## 为什么不用现成的 VS MCP 插件

`Unofficial-VS-MCP`、`VS-MCPServer`、`cv4vs-agents` 都是**进程内**方案：工具调用最终要回到 VS 的 UI 线程。于是 VS 一弹模态框，调用就阻塞，只能等人点；RDP 断开、锁屏、VS 重启都会让连接挂掉。

| 场景 | 进程内插件 | dsh2vs |
|---|---|---|
| VS 弹「项目已过期」对话框 | 调用卡住，等人点 | 不经过 VS，没有这个弹窗 |
| RDP 断开 / 锁屏 | UI 自动化失效 | 无影响 |
| VS 崩溃或重启 | 连接断开，要人工恢复 | 服务独立，重连即用 |
| 客户端重启 | 要重新拉起 stdio 代理进程 | 固定一个 URL，一直在 |

结论：**C++ 的加载、编译、调试，没有一样必须由 VS 进程提供**。桥就做桥。

## 与现有 MCP 的关系（不重复造轮子）

生态里已有同类，我们不装作没有。完整对照见下，边界一句话：**如果你只想要"会跑 cdb 的 MCP"，那别用本项目** —— [mcp-windbg](https://github.com/svnscha/mcp-windbg) 更成熟（1700+ star、支持内核与 TTD）。

| 项目 | 关系 |
|---|---|
| mcp-windbg | **调试原语重叠**，它比我们深 |
| 微软官方 WinDbg MCP（WinDbg 内置） | 要 GUI 交互，**无人值守用不了** ← 我们的位置 |
| VS-MCPServer / VS IDE Bridge | 进程内，**VS 必须运行** |
| msbuild-mcp-server | **构建原语重叠**，但没有"编译完接着调试" |
| **dsh2vs** | 从 `.sln` 出发的无人值守闭环 + 诊断层 |

三条只有我们做的：

1. **不依赖任何 GUI** —— 整条链路都不碰 VS：构建走 MSBuild CLI，调试走 cdb 子进程。
2. **工程感知** —— 别人给你 debugger，你得自己告诉它 exe 在哪；我们从解决方案和配置推出来。
3. **诊断层** —— 启动失败直接给 `0xC0000135` + 缺哪个 DLL；卡在弹窗直接判定"CRT 断言，不是死锁"；崩溃自动 dump + 报告。

## 文档

| 想看 | 去看 |
|---|---|
| 全部 40+ 个工具怎么用 | [docs/tools.md](docs/tools.md) |
| 配置项、多工作区、让它常驻 | [docs/configuration.md](docs/configuration.md) |
| 常见问题（12 条）、已知限制、源码构建细节 | [docs/faq.md](docs/faq.md) |
| 目录结构、路线图、给 agent 的 Skill | [docs/development.md](docs/development.md) |
| 架构决策记录（ADR-001…009） | [docs/design.md](docs/design.md) |
| 面向 AI agent 的完整操作流程 | [skills/dsh2vs/SKILL.md](skills/dsh2vs/SKILL.md) |

## 反馈与许可

- 开 [issue](https://github.com/aaalzk/dsh2vs/issues)：bug 请附上 `healthz` 输出、VS 版本、`artifacts/` 下的构建日志片段。
- MIT © 2026 aaalzk

### GitHub 直连不通？

作者搭建了一个加速服务：**<https://gh.223456.xyz/>**

服务本身只是**部署**：跑的是第三方开源的 GitHub 代理镜像程序，Docker 镜像来自该上游项目，
非本项目开发或构建，作者只提供部署与带宽，不对其实现与安全性背书。

```powershell
git clone https://gh.223456.xyz/github.com/aaalzk/dsh2vs.git
git clone https://gh.223456.xyz/github.com/<任意仓库>.git
```

支持 `git clone` / `git ls-remote` / raw 文件下载；**网页浏览不支持**（会返回 400），看网页请走原站。

