# 发布 npm 包（dsh plugin 命令行安装）

目标：让 `dsh plugin --profile web add dsh2vs` 可用，并进入 DSH 内的「插件中心」。

## 现状

- 包名 **`dsh2vs`** 在 npmjs **可用**（查询 `https://registry.npmjs.org/dsh2vs` → 404）
- 打包内容已验证：8 个文件 / 14.5 kB
  ```
  LICENSE  README.md  package.json
  cordis.patch.yml  index.js  icon.svg  locale/en.json  locale/zh.json
  ```
- **本机 npm 未登录**（`npm whoami` → ENEEDAUTH），所以还不能发布

## 两个环境坑（这台机器上真实存在）

1. **registry 指向国内镜像**：`~/.npmrc` 里是 `registry=https://registry.npmmirror.com`。
   镜像是只读代理，**发布必须显式打官方源**（`publishConfig.registry` 已写死，但命令行再带一次更保险）。
2. **代理**：`~/.npmrc` 里 `proxy` / `https-proxy` 都指向 `http://127.0.0.1:8100`。发布时保持开启。

## 发布步骤

```powershell
cd D:\Projects\dsh2vs

# 1) 登录（交互式：浏览器或 OTP，需要你在场）
npm login --registry https://registry.npmjs.org

# 2) 确认身份
npm whoami --registry https://registry.npmjs.org

# 3) 打包预演（不真的发）
npm publish --dry-run --registry https://registry.npmjs.org

# 4) 发布
npm publish --registry https://registry.npmjs.org

# 5) 验证
npm view dsh2vs version --registry https://registry.npmjs.org
```

发布后立刻可用的命令：

```powershell
dsh plugin --profile web add dsh2vs
```

## 发布后要同步的地方

- README「作为 DSH 插件安装」：把 `github:aaalzk/dsh2vs` 换成（或并列）`dsh2vs`
- README「第 4 步：接到 DSH」的方式 A
- `docs/marketplace-submission.md` 的安装命令与待办勾选
- 版本号：后续改动记得 `npm version patch` 再发，npm 不允许覆盖已发布版本

## 如果不想用 npm 账号

- 只能用源码安装：`dsh plugin --profile web add github:aaalzk/dsh2vs`（现在就能用）
- 代价：进不了 DSH 内的「插件中心」，只能在网页市场被收录

## 版本号约定

当前 `0.1.0`。建议：

| 场景 | 版本 |
|---|---|
| 只改 README / 文案 | patch（0.1.1） |
| 加工具、加诊断能力 | minor（0.2.0） |
| 破坏配置或工具签名 | major（1.0.0） |
