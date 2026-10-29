# dsh2vs

**[English](README_EN.md) · [中文](README.md)**

[![npm version](https://img.shields.io/npm/v/dsh2vs.svg)](https://www.npmjs.com/package/dsh2vs)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**Let your AI assistant (DSH, or any MCP client) actually drive a Visual Studio C++ project: load it, build it, set breakpoints, and read crash reports.**

No VSIX. No Visual Studio required. No "project is out of date, rebuild?" dialog waiting for someone to click it.

> ### ⚠️ Unofficial, independent third-party project
> dsh2vs is a personal project by an independent developer, [aaalzk](https://github.com/aaalzk).
> **It is not affiliated with, endorsed by, or derived from DeepSeek AI, Microsoft, or the Model
> Context Protocol project.** The "dsh" in the name refers to DeepSeek Harness (the tool it talks
> to) and "vs" to Visual Studio (the product it drives) — both are **third-party products called
> from the outside**. This project does not ship, represent, or bundle either of them.

---

## Install: two steps — bridge first, then DSH

dsh2vs is **two independent pieces**. Installing only one leaves DSH pointing at an address nothing listens on.

| Piece | What it is | How to get it |
|---|---|---|
| **① The bridge** | A .NET process that actually runs MSBuild, drives cdb, and analyses crashes | Download the portable build, or build from source |
| **② The DSH plugin** | A config-only package that tells DSH "the bridge is at 127.0.0.1:8790" | `dsh plugin --profile web add dsh2vs` |

```
DSH  ──MCP──▶  ② plugin (six lines of YAML: which number to dial)
                    │
                    ▼  HTTP
                ① bridge (the .NET process that does the work)
                    ├─▶ MSBuild.exe      build
                    ├─▶ cdb.exe          debug
                    └─▶ crash dumps + reports
```

### Step 1: start the bridge

**A. Portable build (recommended — no .NET SDK, no git, no compile)**

Download `dsh2vs-win-x64.zip` from [Releases](https://github.com/aaalzk/dsh2vs/releases), unzip, and run:

```powershell
.\Dsh2Vs.Server.exe --Dsh2Vs:WorkspaceRoot=D:\Projects\YourCppRepo
```

**B. From source (only if you plan to change the code)**

```powershell
git clone https://github.com/aaalzk/dsh2vs.git
cd dsh2vs; dotnet build
.\scripts\start.ps1 -Workspace D:\Projects\YourCppRepo
```

### Step 2: connect DSH

```powershell
dsh plugin --profile web add dsh2vs
```

(If npm is unreachable: `dsh plugin --profile web add github:aaalzk/dsh2vs`)

**Restart the DSH session** — MCP connections load at session start only. The `mcp__vs__*` tools then appear.

> ⚠️ Plugin installed but bridge not running → DSH reports a **loud error** (`failOnStartupError: true`),
> it does not silently drop tools. So: confirm `healthz` says `ok` *before* installing the plugin.

### Confirm the bridge is alive

```powershell
(Invoke-WebRequest http://127.0.0.1:8790/healthz -UseBasicParsing).Content
```

```json
{"status":"ok","name":"dsh2vs","version":"0.1.0","msbuild":"...MSBuild.exe"}
```

> `status: degraded` means MSBuild was not found — see [the FAQ](docs/faq.md).

---

## What it is (30-second version)

- A **small background service** that turns the three C++ things VS does into HTTP endpoints: **load projects / build / debug**.
- An AI assistant calls it over MCP, so it can edit code, build, set breakpoints, and read stacks and locals without anyone at the keyboard.
- It is **not** a VS extension, **not** a chatbot inside VS, and **not** a replacement for VS — open VS whenever you want to look at the UI.

**Good fit**: unattended or remote machines running C++ projects; the "build fails → fix code → build again" loop; escaping "a dialog appeared and now automation is stuck".

**Poor fit**: you only want to chat with AI inside VS (use Claude Code / cv4vs-style extensions); you need the VS debugger's graphical windows; you need managed .NET debugging.

## Why not the existing VS MCP plugins

`Unofficial-VS-MCP`, `VS-MCPServer`, and `cv4vs-agents` are all **in-process**: tool calls eventually land on the VS UI thread. So when VS shows a modal dialog the call blocks until someone clicks; an RDP disconnect, a locked screen, or a VS restart all break the connection.

| Scenario | In-process plugin | dsh2vs |
|---|---|---|
| VS shows "project is out of date" | call blocks, waits for a click | never touches VS, no such dialog |
| RDP disconnect / locked screen | UI automation dies | unaffected |
| VS crashes or restarts | connection drops, manual recovery | service is independent, reconnects |
| client restart | must respawn the stdio proxy | one fixed URL, always there |

Conclusion: **nothing about loading, building, or debugging C++ requires the VS process**. A bridge should be a bridge.

## How it relates to existing MCP servers

Similar projects exist and we are not going to pretend otherwise. The boundary in one sentence: **if all you want is "an MCP that runs cdb", do not use this project** — [mcp-windbg](https://github.com/svnscha/mcp-windbg) is more mature (1.7k stars, kernel and TTD support).

| Project | Relationship |
|---|---|
| mcp-windbg | **overlaps our debugging primitives**, and goes deeper |
| Microsoft's official WinDbg MCP (built into WinDbg) | needs GUI interaction — **useless unattended** ← our spot |
| VS-MCPServer / VS IDE Bridge | in-process, **VS must be running** |
| msbuild-mcp-server | **overlaps our build primitive**, but stops at building |
| **dsh2vs** | an unattended loop starting from `.sln`, plus a diagnosis layer |

Three things only we do:

1. **No GUI anywhere in the loop** — builds go through the MSBuild CLI, debugging through a cdb child process. Not "works with VS closed", but "never touches VS".
2. **Solution-aware** — other servers hand you a debugger and expect you to say where the exe is; we derive it from the solution and configuration.
3. **A diagnosis layer** — startup failure returns `0xC0000135` *and the missing DLL*; a blocked target is called out as "CRT assertion, not a deadlock" with the assertion text; crashes get an automatic dump and report.

## Documentation

| Looking for | Go to |
|---|---|
| All 40+ tools | [docs/tools.md](docs/tools.md) |
| Configuration, multi-workspace, running as a service | [docs/configuration.md](docs/configuration.md) |
| FAQ (12 entries), known limits, building from source | [docs/faq.md](docs/faq.md) |
| Repo layout, roadmap, agent skill | [docs/development.md](docs/development.md) |
| Architecture decision records (ADR-001…009) | [docs/design.md](docs/design.md) |
| The full agent-facing runbook | [skills/dsh2vs/SKILL.md](skills/dsh2vs/SKILL.md) |

## Feedback and license

- Open an [issue](https://github.com/aaalzk/dsh2vs/issues): for bugs include the `healthz` output, your VS version, and the build log fragment from `artifacts/`.
- MIT © 2026 aaalzk

### github.com slow or unreachable?

The author runs an accelerator: **<https://gh.223456.xyz/>**

It is a **deployment only**: it runs a third-party open-source GitHub proxy image (the Docker image comes from that upstream project, not developed or built here). The author supplies the hosting and bandwidth, and does not vouch for its implementation or security.

```powershell
git clone https://gh.223456.xyz/github.com/aaalzk/dsh2vs.git
git clone https://gh.223456.xyz/github.com/<any-repo>.git
```

Supports `git clone` / `git ls-remote` / raw file download. **Browsing web pages is not supported** (returns 400) — use github.com for that.
