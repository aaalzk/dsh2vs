# 工具清单与完整流程

> 上一篇：[README](../README.md) · 相关：[配置](configuration.md) · [常见问题](faq.md)

dsh2vs 通过 MCP 暴露以下工具，客户端里显示为 `mcp__vs__*`（serverName = `vs`）。

## 工具清单

**工程与构建**

| 工具 | 作用 |
|---|---|
| `project_open` | 读 `.sln` / `.vcxproj`，返回项目、配置、平台；成为后续默认目标。**不启动 VS。** |
| `project_info` | MSBuild 求值：输出 exe/dll 的 `TargetPath`、`OutDir`、`IntDir`、工程类型 |
| `project_configs` | 列出配置与平台 |
| `project_config_set` | 设定后续默认的 `Configuration` / `Platform` |
| `build` / `rebuild` / `clean` | MSBuild 编译 / 重建 / 清理，返回结构化诊断 |
| `build_errors` | 重读上次构建的错误，不必重新编译 |
| `vs_status` | 服务状态、MSBuild 路径、当前解决方案、上次构建结果 |

**调试（基于 cdb，beta）**

| 工具 | 作用 |
|---|---|
| `debug_launch` / `debug_attach` | 启动 exe（停在初始断点）或附加到 PID。**启动失败会返回 `ok:false` + 退出码 + 缺失依赖清单**，不再是「它退出了」 |
| `debug_preflight` | 启动前检查依赖：读 PE 导入表，逐个在 exe 目录 / System32 / PATH 里解析，直接列缺失的 DLL（毫秒级，不起调试器） |
| `debug_continue` / `debug_step` | 继续 / 单步（into·over·out） |
| `debug_break` | 打断。卡在模态框时 `.break` 不生效，会自动用 `DebugBreakProcess` 注入；注入断点落在一次性线程上，所以它会**自动切到真正卡住的那个线程**，并给出结论式 `diagnosis`（如「CRT 断言，不是死锁」+ 断言原文） |
| `debug_thread_switch` | 切换当前线程（`~Ns`）；之后 `stack_get` / `locals_get` / `evaluate` 都作用于它 |
| `breakpoint_set` / `list` / `remove` | 断点，支持条件表达式；符号没加载时自动降级为 `bu` |
| `exception_set` / `exception_list` | 异常断点：`sxe/sxd/sxn/sxi`，比如 `event=av mode=break` 让程序在第一次访问越界时就停 |
| `stack_get` | 调用栈，解析成 `{frame, returnAddress, symbol, file, line}`；支持 `threadId=` 单次指定线程 |
| `locals_get` | 局部变量（含类型与地址）；支持 `threadId=` |
| `evaluate` | C++ 表达式求值 |
| `threads_get` / `modules_get` / `memory_read` | 线程 / 模块 / 内存 |
| `debug_stop` | 结束会话并杀掉被调试进程（不留孤儿） |
| `debug_execute` | 逃生口：直接发任意 cdb 命令（`.exr -1`、`k`…） |

**崩溃取证（M3）**

| 工具 | 作用 |
|---|---|
| `dump_save` | 给当前被调试进程写完整 minidump（`.dump /ma`），默认存 `%LOCALAPPDATA%\dsh2vs\dumps` |
| `crash_analyze` | 不给参数就分析当前会话；给 `dumpPath` 就在一次性进程里分析 dump。返回异常码、异常名、故障模块、故障帧、失败桶 |
| `debug_open_dump` | 把 .dmp 打开成当前会话，之后 `stack_get` / `locals_get` / `threads_get` 都能对着崩溃现场用 |

> 崩溃分析以**异常记录**（`.exr -1` / `.ecxr`）为准，不依赖系统符号，通常 0.1–1.5 秒返回。
> `!analyze -v` 只有在配置了 `Debug:SymbolServer` 时才会跑 —— 否则它会去联网拉系统符号，
> 卡几分钟还给出 `WRONG_SYMBOLS` 的错误结论（这正是 VS 里常见的那个卡点）。

**VS 联动（M4，需要你的 Visual Studio 正在运行）**

| 工具 | 作用 |
|---|---|
| `vs_dte_status` | 读 VS 当前状态：打开的解决方案、工程数、构建状态、调试器模式 |
| `vs_dte_open` | 在**你的 VS 窗口里**打开某个解决方案（你想看/想改时用） |
| `vs_dte_build` | 用 VS 自己构建当前解决方案，返回 VS 的构建结果 |
| `vs_dte_errors` | 读 VS 的「错误列表」，结构化返回 |
| `vs_dte_attach` | 把进程交给 VS 调试器，方便你旁观。**桥正用 cdb 调试该进程时会拒绝**（一个进程不能被两个调试器挂） |

> 这一层走 DTE（COM），通过 Windows PowerShell 5.1 调 —— 不改你的 VS 状态就没有副作用；
> 但 `vs_dte_open` / `vs_dte_build` 确实会改变 VS 里打开的东西，无人值守时优先用普通的 `build` 工具。

---

## 走一遍完整流程（用自带的 C++ 样例，约 2 分钟）

仓库里带了一个小样例，可以先拿它验证整条链路通不通：

```powershell
# 生成 VS 解决方案（需要 CMake）
cmake -S samples/hello-cpp -B samples/hello-cpp/build -G "Visual Studio 17 2022" -A x64
```

然后让 AI 依次做这几件事（或者你自己按顺序点工具）：

```
project_open  → samples/hello-cpp/build/hello-cpp.sln     # 读到 4 个项目、4 个配置、x64
build                                                      # 编译，返回 succeeded
debug_launch  → samples/hello-cpp/build/Debug/loopy.exe    # 停在初始断点
breakpoint_set→ loopy!main                                 # 下断点
debug_continue                                             # 命中断点，state=stopped
stack_get                                                  # 看到调用栈
locals_get                                                 # 看到 argc / argv / waitMode
debug_stop                                                 # 结束，进程不残留
```

预期结果（本机实测）：

```
stack_get  → loopy!main+0x5 / loopy!invoke_main+0x39 / loopy!__scrt_common_main_seh+0x132 …
locals_get → int argc = 0n1 ; char ** argv = 0x0000018f`55667150 ; bool waitMode = false
```
