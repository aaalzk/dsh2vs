# dsh-plugin.org 收录申请（存档）

提交于 dsh-plugin.org 的插件市场，issue：<https://github.com/dshplugin/dsh-plugin-hub/issues/502>

状态：`open`（等待审核）。收录后把详情页地址回填到 README 的徽章注释里。

## 本站要求 → 本仓库对应物

| dsh-plugin.org 要求 | 本仓库 |
|---|---|
| 公开仓库 | <https://github.com/aaalzk/dsh2vs> |
| GitHub topic `dsh-plugin` | 已添加（另有 mcp / visual-studio / cpp / msbuild / debugger / windows） |
| README 含安装命令 | `dsh plugin --profile web add dsh2vs` |
| 导出 `apply(ctx)` | `index.js`（配置型 bundle，`apply` 为空实现，业务在 .NET 桥进程） |
| 根目录 `package.json` | 已添加，声明 `dsh.bundle.patch` |
| 能力分类（11 类选一） | 集成与连接 |
| 许可证 | MIT |
| 信任信号 | README 的「作为 DSH 插件安装」表格 + 真实 JSON 输出 + 权限/外部服务说明 |

## 提交正文（与 issue #502 一致）

### 仓库地址
https://github.com/aaalzk/dsh2vs

### 一句话价值
装完之后 DSH 能直接加载 VS 的 C++ 解决方案、用 MSBuild 编译、用 cdb 下断点看调用栈与变量，
并在崩溃或启动失败时自己给出结论（缺哪个 DLL / 是不是 CRT 断言）；不需要装 VSIX、不需要
Visual Studio 开着、不会被「项目已过期，是否重新生成」这类模态框卡死。

### 能力分类
集成与连接

### 安装命令
```
dsh plugin --profile web add dsh2vs
```

### 兼容与运行要求
- DSH：任意近期版本（配置型 bundle，注入 `@deepseek-ai/dsh-mcp-client` 指向 `http://127.0.0.1:8790/mcp`）
- 平台：Windows 10/11 x64（依赖 cdb.exe 与 MSBuild）
- 运行环境：.NET 9 SDK；VS 2022 C++ 工具链；Windows SDK 的 Debugging Tools for Windows
- **桥要先启动**：`.\scripts\start.ps1 -Workspace <你的C++工程目录>`，默认监听 127.0.0.1:8790

### 补充说明
- **插件形态**：配置型 bundle。业务在独立的 .NET 桥进程里（刻意不进程内：进程内的 VS 方案会阻塞在
  VS UI 线程，一弹模态框就废了）。`apply(ctx)` 空实现是官方 mcp-bundle 规范的做法。
- **权限**：读写工作区内的工程与构建产物；以子进程方式启动 `MSBuild.exe` 与 `cdb.exe`
- **外部服务**：只连本机 `127.0.0.1:8790`；无遥测、不上传代码；默认不连符号服务器
- **已知限制**：仅 Windows + C++/MSBuild；托管 .NET、混合模式调试未覆盖
- 若之前手动配置过 `serverName: vs` 的 MCP，请先删除该条再装插件，避免重复注册

## 待办

- [ ] 审核通过后：把 `https://dsh-plugin.org/plugins/aaalzk/dsh2vs` 回填到 README 徽章
- [x] NPM 包已发布（`dsh2vs@0.1.0`），安装命令已是 `dsh plugin --profile web add dsh2vs`，
      才会出现在 DSH 内的「插件中心」
- [ ] 补一张真实截图或 GIF（Plugin Manager 里的插件条目 + 一次 build→debug 会话）


