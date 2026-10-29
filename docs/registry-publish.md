# 上架官方 MCP Registry（registry.modelcontextprotocol.io）

> **状态：已收录** ✅ —— `io.github.aaalzk/dsh2vs@0.1.0`，2026-10-29 发布。
> 本文保留为**下次发版**的 runbook，以及踩过的坑。

## 收录结果（实测）

```
GET /v0/servers?search=dsh2vs        -> total=1     （发布前是 0）
GET /v0/servers/io.github.aaalzk%2Fdsh2vs/versions/0.1.0
  name        io.github.aaalzk/dsh2vs
  title       dsh2vs — VS C++ bridge (unofficial)
  description Unofficial, independent (not DeepSeek AI/Microsoft). VS C++ build+debug via MSBuild and cdb.
  packages[0] mcpb -> .../releases/download/v0.1.0/dsh2vs-0.1.0.mcpb
  fileSha256  47746eb0…（与本地算的一致）
```

⚠️ **Registry 的搜索按 name 匹配，不全文检索描述**：`search=visual studio` 和 `search=msbuild` 都是 0。
要提高被发现率得靠做全文检索的第三方目录（Smithery / Glama / mcp.so）。

## 为什么一开始发不了

公共索引只认两种形态，我们两种都不满足：

| 形态 | 要求 | 我们 |
|---|---|---|
| `remotes` | URL 必须**公网可达** | 只听 `127.0.0.1:8790`，且不应对外 |
| `packages` | 真实发布到 npm / PyPI / NuGet / OCI / **MCPB** | npm 包是 **DSH 配置插件**，不是可执行的 MCP 服务器二进制 —— 登记上去名不副实 |

所以必须先补 **stdio 传输**（本地 MCP 服务器的惯例形态），否则包打出去客户端按 stdio 通信而我方沉默。

## 下次发版的完整步骤

### 1. 确认 stdio 模式还在

```powershell
.\Dsh2Vs.Server.exe --stdio
```

stdio 下 **stdout 是协议通道**：`Program.cs` 里 `Logging.ClearProviders()` + `AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)`，
日志全走 stderr。新增代码时**不要往 stdout 写任何东西**（`Console.Write` 会直接污染协议）。

### 2. 打 MCPB

```powershell
dotnet publish src\Dsh2Vs.Server\Dsh2Vs.Server.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts\publish\single
```

> ⚠️ **必须加 `-p:PublishSingleFile=true`**。只加 `--self-contained` 时，产出的 exe 是 **156 KB 的 apphost 桩**，
> 运行时是旁边 348 个独立文件 —— 这样打出来的 MCPB 只有 68 KB 且**根本跑不起来**。
> 单文件后 exe 103.8 MB，压缩成 MCPB 约 43 MB。

组装目录（`artifacts/mcpb/`）：`manifest.json` + `Dsh2Vs.Server.exe` + `appsettings.json` + `README.txt`。

`manifest.json` 要点（spec `0.3`，2025-12-02）：

```jsonc
{
  "manifest_version": "0.3",
  "name": "dsh2vs",
  "version": "1.0.0",
  "author": { "name": "aaalzk" },
  "server": {
    "type": "binary",
    "entry_point": "Dsh2Vs.Server.exe",
    "mcp_config": {
      "command": "${__dirname}/Dsh2Vs.Server.exe",
      "args": ["--stdio"],
      "env": {}
    }
  },
  "compatibility": { "platforms": ["win32"], "runtimes": {} }
}
```

> ⚠️ **不要声明 `user_config` 却不在 `args` 里引用** —— 宿主会弹一个不起作用的配置框。宁可没有。

打包（`Compress-Archive` 只认 `.zip`，所以先打 zip 再改名）：

```powershell
Compress-Archive -Path artifacts\mcpb\* -DestinationPath artifacts\dsh2vs-1.0.0.zip
Move-Item artifacts\dsh2vs-1.0.0.zip artifacts\dsh2vs-1.0.0.mcpb -Force
```

### 3. 挂 GitHub Release 并回填哈希

上传后用 **小写** sha256 填回 `server.json` 的 `packages[0].fileSha256`：

```powershell
(Get-FileHash artifacts\dsh2vs-1.0.0.mcpb -Algorithm SHA256).Hash.ToLowerInvariant()
```

> ⚠️ schema 要求 `^[a-f0-9]{64}$`（**小写**），而 `Get-FileHash` 返回大写，直接填会被拒。

### 4. 校验 + 发布

```powershell
mcp-publisher validate        # 服务端校验，应输出 ✅ server.json is valid
mcp-publisher login github    # 设备码，必须人工交互
mcp-publisher publish
```

CLI 装在 `%LOCALAPPDATA%\Programs\mcp-publisher\`（已加入用户 PATH）。
**没有 PAT 通道**，只有交互式设备码或 CI 的 `github-oidc`；在非交互 shell 里跑只会报 `EOTP` 且 URL 被打码成 `***`。

## 字段级约束（都踩过）

| 字段 | 约束 |
|---|---|
| `description` | **上限 100 字符**，超出返回 `422 Unprocessable Entity` |
| `fileSha256` | **小写**十六进制 |
| CLI 认证 | 仅交互式设备码 / `github-oidc`；非交互必失败 |

## PowerShell 编码坑（本会话反复踩）

- `ConvertFrom-Json` / `Get-Content -Raw` 在 PS 5.1 下用 **ANSI** 读 UTF-8 中文 → 中文 JSON 假报"解析失败"但文件是好的。
  **验中文 JSON 一律用 `node -e "JSON.parse(fs.readFileSync(...))"`**
- `Compress-Archive` / `Expand-Archive` 只认 `.zip`；`.mcpb` / `.nupkg` 先打 zip 再改名
- `Set-Content -Encoding UTF8` 会加 **BOM**，Go 解析器（mcp-publisher）直接拒；写这类文件要用能控编码的工具

## 备选曝光渠道（不要求 stdio）

- **Smithery**：支持从 GitHub 仓库托管/描述服务器，可接受 HTTP 型
- **Glama / mcp.so**：收录 GitHub 仓库，主要看 README
- **awesome-mcp-servers**：提 PR 加一行
