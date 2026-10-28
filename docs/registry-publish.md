# 发布到官方 MCP Registry（registry.modelcontextprotocol.io）

状态：`server.json` 已通过官方校验（`mcp-publisher validate` → 无错误）。
**尚未发布**，卡在一个产品缺口和一个需要人参与的步骤上，见下。

## 已完成

- 仓库根目录 `server.json`（schema `2025-12-11`），命名空间 `io.github.aaalzk/dsh2vs`
- 校验命令与结果：

```powershell
# 下载 CLI（约 7.5 MB，需要代理）
# https://github.com/modelcontextprotocol/registry/releases/download/v1.8.6/mcp-publisher_windows_amd64.tar.gz
mcp-publisher validate      # 服务端校验，通过
```

校验过程中踩到的坑：`description` **最长 100 字符**，超了返回 `422 Unprocessable Entity`。

## 卡点 1（产品缺口，必须先做）：没有 stdio 传输

官方 Registry 只接受两种形态：

| 形态 | 要求 | 我们的现状 |
|---|---|---|
| 远程服务器（`remotes`） | URL 必须**公网可访问** | 本机 `127.0.0.1:8790`，不满足 |
| 包（`packages`） | 真实发布到 npm / PyPI / NuGet / OCI / **MCPB**，或直接下载 URL + sha256 | 我们只有源码 |

而所有「本地 MCP 服务器」的惯例形态是 **stdio**：客户端 spawn 你的 exe，用 stdin/stdout 说 JSON-RPC。
dsh2vs 目前只讲 HTTP（streamable-http），所以：

- MCPB 包装上去之后，客户端会按 stdio 通信 → 我们不该沉默
- Claude Desktop / Cursor / Cline 的本地服务器默认也是 stdio

**结论：先给服务加 `--stdio` 模式**（.NET MCP SDK 自带 `StdioServerTransport`，改动很小），
再谈打包与发布。加了之后顺带解锁：MCPB 包、`command: dsh2vs.exe` 的极简配置、以及各客户端的
「本地 MCP 服务器」列表。

## 卡点 2（需要人参与）：GitHub 设备码登录

```powershell
mcp-publisher login github    # 会打印类似 ABCD-1234 的码
# 打开 https://github.com/login/device 输入该码并授权
mcp-publisher publish
```

CLI 只有交互式 OAuth（`github`）或 CI 用的 `github-oidc`；没有 PAT 直传的方式。
所以这一步必须你在场，或者放进 GitHub Actions（`github-oidc`，无需人工）。

## 发布路线（推荐顺序）

1. **加 `--stdio` 模式**（30~50 行）
2. **打包 MCPB**：`dotnet publish -r win-x64 --self-contained` 产出 → 连同 `manifest.json` 打成
   `dsh2vs-0.1.0.mcpb` → 挂到 GitHub Release
3. 把 Release 资产的 **真实 sha256** 填回 `server.json` 的 `packages[0].fileSha256`
4. `mcp-publisher login github` → `mcp-publisher publish`

`server.json` 现在用的是占位 sha（全 0），第 3 步必须替换，否则客户端完整性校验会失败。

## 备选：不动产品也能曝光

- **Smithery**：支持从 GitHub 仓库托管/描述服务器，可接受 HTTP 型
- **Glama / mcp.so**：收录 GitHub 仓库，主要看 README
- **awesome-mcp-servers**：提 PR 加一行

这些都不要求 stdio，优先级可以排在 stdio 之前用来先拿到流量。
