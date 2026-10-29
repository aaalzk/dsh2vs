# 开发与路线图

> 上一篇：[README](../README.md) · 相关：[工具清单](tools.md) · [配置](configuration.md)

## 目录结构

```
src/Dsh2Vs.Core     公共模型、进程执行、MSBuild 定位
src/Dsh2Vs.Build    MSBuild 调用、日志解析、解决方案解析
src/Dsh2Vs.Debug    cdb 会话、调试会话管理、输出解析
src/Dsh2Vs.Server   MCP 服务（streamable-http）、各工具定义
tests/              单元测试（解析器为主）
samples/hello-cpp   CMake 示例工程（构建 + 调试的验收目标）
scripts/            start.ps1 / install-service.ps1 / watchdog.ps1
```


---

## 路线图

- ✅ **M1** 项目加载、构建、结构化诊断、配置切换
- ✅ **M2（beta）** cdb 调试：启动/附加、断点、单步、栈、变量、求值、线程、模块、内存
- ✅ **M3** 崩溃取证：`dump_save`、`crash_analyze`、`debug_open_dump`（以异常记录为准，秒级返回）
- ✅ **M4（beta）** VS 联动：`vs_dte_*` —— 读 VS 状态、在 VS 里开解决方案、驱动 VS 构建、读错误列表、调试交接
- ⬜ **M5（可选，已实现可切换）** dbgeng 进程内引擎：`--Dsh2Vs:Debug:Engine=dbgeng` 即可切换。
  去掉子进程与提示符解析，拿结构化控制；代价是引擎崩溃会连带服务，所以默认仍是 cdb（子进程有故障隔离）。
  停止时用引擎自身的 `.kill` 终止目标（`HasExited=true`），但那个已终止的进程对象要等服务退出才从列表消失，属正常现象


---

## 给 agent 的操作手册（Skill）

仓库里带了 `skills/dsh2vs/SKILL.md` —— 面向 AI agent 的完整操作流程：编译回路、调试回路、
卡在弹窗怎么办、崩溃取证、VS 联动、工作区规则，以及一张踩过的坑对照表。

装到 DSH：把这个目录放到 `~/.dsh/skills/` 下（或直接让 agent 读文件路径），
下次遇到 C++ 构建/调试任务就会照着流程走，不用每次重新摸索。
