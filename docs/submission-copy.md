# 提交帖文案：怎么写才不会被误认成"对面的东西"

两家站点各有各的风险点，文案也不同。核心原则一句话：

> **不要只说"我不是官方的"，要说出我是谁、我调用谁、以及名字里的每个词指向什么。**
> 只说"非官方"读者会以为你在故作姿态；说清楚命名来源，误解才真的消失。

下面两份都可以直接复制使用。

---

## 一、给 dsh-plugin.org 的提交帖

（原 #502 已被 bot 自动关闭并说明"全自动扫描、无需操作"。如果下一轮扫描后仍未收录，用下面这帖重开一条。）

**标题**

```
[插件提交] aaalzk/dsh2vs — 第三方独立插件：让 DSH 编译并断点调试 VS 的 C++ 工程（非官方）
```

**正文**

```markdown
### 仓库地址
https://github.com/aaalzk/dsh2vs

### 身份说明（请先看这条）
本项目是**独立第三方开发者 aaalzk 的个人项目**，与 DeepSeek AI、Microsoft 均无隶属关系，
也未获其认可或背书。名字里的 "dsh" 指 DeepSeek Harness（本项目对接的工具），
"vs" 指 Visual Studio（本项目从外部驱动的产品）—— 两者都只是被调用的第三方产品，
本项目不发行、不代表、不打包其中任何一个。npm 包名 `dsh2vs` 是个人占名，非官方保留名。

### 一句话价值
装完之后 DSH 能直接加载 VS 的 C++ 解决方案、用 MSBuild 编译、用 cdb 下断点看调用栈与变量，
并在崩溃或启动失败时自己给出结论（缺哪个 DLL / 是不是 CRT 断言）；不需要装 VSIX、
不需要 Visual Studio 开着、不会被「项目已过期」这类模态框卡住。

### 能力分类
集成与连接（它本质是一座桥：把 DSH 接到一台 Visual Studio 工作站）

### 安装命令
```
dsh plugin --profile web add dsh2vs
```
（npm 不通时：`dsh plugin --profile web add github:aaalzk/dsh2vs`）

### 兼容与运行要求
- DSH 任意近期版本（配置型 bundle，注入 `@deepseek-ai/dsh-mcp-client` 指向 `http://127.0.0.1:8790/mcp`）
- 平台：Windows 10/11 x64
- 桥依赖：VS 2022/2026 C++ 工具链（MSBuild）、Windows SDK 的 Debugging Tools（cdb.exe）
- 绿色版不需要 .NET SDK；源码构建需要 .NET 9 SDK

### 许可证
MIT

### 截图 / 演示
暂无截图。以下是工具真实返回值（可直接复现）：

启动失败（缺依赖 DLL）不再只说「它退出了」：
```json
{"ok":false,"state":"exited","exitCode":-1073741515,"exitCodeHex":"0xC0000135",
 "diagnosis":"STATUS_DLL_NOT_FOUND: a dependency DLL is missing, the program never started",
 "imports":{"missingCount":1,"missing":["payload.dll"]}}
```

卡在弹窗时直接给结论：
```json
{"state":"stopped","threadCount":4,"selectedThread":0,
 "diagnosis":{"kind":"crt-assertion-dialog","windowTitle":"Microsoft Visual C++ Runtime Library",
   "assertion":"File: ...\\include\\vector | Line: 1931 | Expression: vector subscript out of range",
   "summary":"target is blocked in a modal dialog ... = CRT assertion/abort, NOT a deadlock."}}
```

### 补充说明
- **插件形态**：配置型 bundle。`cordis.patch.yml` 只负责把 MCP 客户端这一行接进 profile，
  业务在独立的 .NET 桥进程里（刻意不进程内：进程内的 VS 方案会阻塞在 VS UI 线程，
  一弹模态框就废了）。所以 `index.js` 的 `apply(ctx)` 是空实现，这是官方 mcp-bundle 规范的做法。
- **与官方/微软工具的关系**：微软官方有 WinDbg MCP（需在 WinDbg 界面里交互），
  生态里有 mcp-windbg 等成熟调试器 MCP。本项目定位不同：**无人值守、不开任何 GUI**。
  README 里有完整的对照表，也明确写了"只想要会跑 cdb 的 MCP 就别用本项目"。
- **权限**：只监听 127.0.0.1；读写工作区内的工程与构建产物；以子进程方式启动 MSBuild.exe 与 cdb.exe
- **外部服务**：只连本机 `127.0.0.1:8790`；无遥测、不上传代码；默认不连符号服务器
- **已知限制**：仅 Windows + C++/MSBuild；托管 .NET、混合模式调试未覆盖

### 提交前自查
- [x] 仓库已公开
- [x] 已添加 GitHub topic：dsh-plugin
- [x] README 包含安装命令
- [x] 插件导出 apply(ctx) 模块
- [x] 仓库根目录包含 package.json
```

---

## 二、给 MCP 官方 Registry 的提交说明

（`server.json` 已通过官方 API 校验；发布命令见 `docs/registry-publish.md`。
这段文字用在 GitHub 上的说明 / PR 描述里，**不是**填进 server.json —— 那个字段有 100 字符上限。）

**标题**

```
Add io.github.aaalzk/dsh2vs — unofficial Visual Studio C++ build+debug server
```

**正文**

```markdown
## What this is

An **unofficial, independent** MCP server by aaalzk. It is not affiliated with, endorsed by,
or derived from DeepSeek AI, Microsoft, or the Model Context Protocol project.

The name: `dsh` is DeepSeek Harness (the client it serves), `vs` is Visual Studio (the product
it drives from the outside). Neither is shipped or represented by this project.

## What it does

Builds and debugs Visual Studio C++ projects unattended: resolves a `.sln`, builds with MSBuild,
launches the produced exe under cdb, sets breakpoints, reads stacks and locals, and turns crashes
or startup failures into a verdict. No VSIX and no Visual Studio process anywhere in the loop.

## Relationship to existing servers

- **mcp-windbg** (1.7k stars) and **Microsoft's official WinDbg MCP** both drive the same
  debugger engine and are more mature for interactive debugging. This server is deliberately
  different: it exists for machines where nobody can click a dialog and no GUI can be opened.
- No existing server in the registry covers MSBuild-based C++ builds (searches for
  "visual studio" and "msbuild" currently return zero results).

The README states this plainly, including "if all you want is a cdb wrapper, use mcp-windbg".

## Status / honesty about the entry

`server.json` validates against the registry API. The `packages` entry is an **MCPB placeholder**:
the asset is not published yet, and `fileSha256` is still zeros. It will be replaced with a real
release asset and hash before `mcp-publisher publish` is run, or the entry will be withdrawn.

## Auth

Publishing uses `mcp-publisher login github` (interactive device flow) — no PAT, no org namespace.
```

---

## 三、为什么这样写（可迁移的四条）

| 做法 | 原因 |
|---|---|
| **把身份说明放最前面**，不放在末尾"补充"里 | 审核人和用户都在前 10 行决定要不要继续读；放末尾等于没写 |
| **解释名字里的每个词指向什么** | 这是误解的真正来源。"dsh" 和 "vs" 听起来像官方产品线，说清"只是被我调用的第三方"即可消解 |
| **点名承认更强的同类，并说清自己不同在哪** | 你越遮掩重叠，越像蹭的；主动让位反而建立可信度 |
| **说明"我调用了谁"而非"我属于谁"** | 全篇用「从外部调用」「不发行、不代表、不打包」这类动词，读者自然理解边界 |

### 要避免的四种写法

| 别这么写 | 为什么 |
|---|---|
| "官方 DSH VS 插件" | 直接冒充，dsh-plugin.org 明确不收录 |
| "the VS bridge for DSH" | 定冠词暗示唯一性/官方性 |
| "由 DeepSeek Harness 驱动" | 被动句读起来像隶属关系 |
| 只写"非官方"三个字 | 没有信息量，读者仍然不知道你是谁 |

---

## 四、当前进度与待办

- [x] npm 描述、locale（插件中心卡片）、server.json、两个 README 都已带上声明
- [x] `server.json` 通过官方 API 校验（描述压到 92 字符，上限 100）
- [ ] **发布 npm 0.1.1** —— 本地 token 已失效（401），需重新授权。命令见下
- [ ] dsh-plugin.org 下一轮扫描后确认收录（目前仍是 404）
- [ ] MCP Registry 发布 —— 需先补 stdio 传输 + MCPB 产物 + 真 sha256，且需你插 security key

### 发布 0.1.1（需要你在场）

```powershell
cd D:\Projects\dsh2vs
npm login --registry https://registry.npmjs.org          # 会打印一个 /login?next=/login/cli/... 链接
npm publish --registry https://registry.npmjs.org        # 若再要认证，会打印 /auth/cli/... 链接，插 key
npm view dsh2vs version --registry https://registry.npmjs.org
```
