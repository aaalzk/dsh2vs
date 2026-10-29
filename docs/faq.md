# 常见问题与排障

> 上一篇：[README](../README.md) · 相关：[配置](configuration.md) · [工具清单](tools.md)

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


---

## 已知限制

- 源码行断点在「初始断点」阶段设置可能退化成延迟断点而不生效 —— 建议按符号下断，或第一次停下后再设。
- 函数入口处 `locals_get` 会显示垃圾值（cdb 固有行为，步入函数体后正常）。
- `debug_break` 在目标高速运行时可能延迟返回，随后用 `debug_status` 确认。
- cdb 与 VS 调试器**不能同时**挂同一进程；想自己在 VS 里旁观，先 `debug_stop`。


---

# 附：源码路线细节（用绿色版可跳过）

上面 README 的「安装」是总览，这里是**源码路线**每一步的细节。

### 第 0 步：拿到代码

三种方式，任选其一：

```powershell
# A. 能直连 GitHub
git clone https://github.com/aaalzk/dsh2vs.git

# B. 直连不通 / 很慢 —— 用镜像加速（把 GitHub 地址原样接在镜像域名后面）
git clone https://gh.223456.xyz/github.com/aaalzk/dsh2vs.git

# C. 不想用 git：浏览器打开仓库 → Code → Download ZIP
# D. 不想编译：去 Releases 下载 dsh2vs-win-x64.zip（绿色版，自带 .NET 运行时）
```

> 镜像 `https://gh.223456.xyz/` 是**本项目作者搭建的一个加速服务**：它跑的是第三方开源的 GitHub 代理
> 镜像程序（Docker 镜像由该上游项目提供，非本项目自行开发或构建），作者只负责部署和带宽，不对其
> 实现与安全性背书。不方便走第三方时请直连 GitHub 或用 ZIP 下载。
> 已验证支持 `git clone` / `git ls-remote` / raw 原始文件下载；**网页浏览不支持**（会返回 400），
> 看网页请走正常 GitHub。用法就是把 `https://github.com/...` 换成 `https://gh.223456.xyz/github.com/...`。

### 第 1 步：构建（绿色版跳过）

```powershell
cd dsh2vs
dotnet build
```

### 第 2 步：启动（把路径换成你自己的 C++ 工程目录）

源码路线：

```powershell
.\scripts\start.ps1 -Workspace D:\Projects\YourCppRepo
```

绿色版（解压后直接跑，不需要 .NET SDK）：

```powershell
.\Dsh2Vs.Server.exe --Dsh2Vs:WorkspaceRoot=D:\Projects\YourCppRepo
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
dsh plugin --profile web add dsh2vs
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