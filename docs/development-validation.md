# 开发与验证流程

本文规定 Augit 任务的文件读取、基线记录、分批测试、资源清理和提交登记方式。产品行为仍以 `docs/product-spec.md` 为准，技术边界仍以 `docs/architecture.md` 为准，本文不新增产品能力。

## 1. 修改前的读取顺序

按以下顺序读取；与当前任务无关的模块文档可以在确定影响范围后停止继续展开。

1. `AGENTS.md`
2. `docs/product-spec.md`
3. `docs/architecture.md`
4. `docs/ux-spec.md`
5. `docs/design-system.md`
6. 当前大模块对应的 `docs/nui-behavior/*.md`
7. `docs/nui-behavior/10-backlog.md`
8. `docs/intellij-platform-ui-reference.md` 和 `docs/intellij-platform-ui-behavior.md`
9. 当前工作区的状态、差异统计和受影响文件差异
10. 本地 `/mnt/d/github/intellij-community` 中对应的权威源码，以及已打开 PyCharm 中对应的只读参考页面

`docs/roadmap.md` 仅作为历史记录读取，不参与当前设计、实现或冲突判断。

## 2. 基线记录

开始修改前记录当前分支、提交、工作区改动、空白错误和四个运行时文件哈希：

```powershell
git branch --show-current
git rev-parse HEAD
git status --short
git diff --stat
git diff --check
Get-FileHash -Algorithm MD5 web/src/mockup.js,web/src/mockup.css,web/src/live-data.js,web/src/bridge.js
```

同时运行 `powershell -NoProfile -File tools/audit/verify-ui-assets.ps1`，并记录视觉稿与运行时共享文件两侧哈希。每个批次完成后重新记录这些值，在对应模块文档和 `docs/intellij-platform-ui-behavior.md` 登记修改前后哈希、批次、日期、修改文件、测试命令、耗时、结果、未执行项目和未解决差异。历史哈希不能代替当前实测值。

## 3. 分批测试

小批次只运行受影响范围的验证。连续小批次可以合并，完成一个大模块后必须运行该模块完整测试并验证跨模块接口、状态和页面跳转。

- 网页、CSS 或视觉稿：`verify-ui-assets`、相关 `tools/verify-ux-*.cjs`、相关实时场景、JavaScript 语法检查和 `git diff --check`。
- Core 或 Infrastructure：受影响项目的 `dotnet test`、直接依赖的 bridge 测试和必要构建。
- Shell、WebView2 生命周期或 bridge 协议：Shell 测试、相关 UI 场景、进程退出和资源清理验证。
- 性能或进程管理：目标性能场景、按父进程归属的进程树检查、关闭清理检查。

常用命令：

```powershell
dotnet build Augit.slnx --no-restore
dotnet test tests/Augit.Core.Tests --no-build --no-restore
dotnet test tests/Augit.Shell.Tests --no-build --no-restore
dotnet test tests/Augit.Infrastructure.Tests --no-build --no-restore
powershell -NoProfile -File tools/audit/verify-ui-assets.ps1
powershell -NoProfile -File tools/audit/verify-script-encoding.ps1
node tools/audit/check-doc-claims.cjs
node tools/audit/coverage-generator.test.cjs
node tools/audit/live-shell.spec.cjs <Windows 下可解析的 Playwright 模块路径>
git diff --check
```

`live-shell` 和相关脚本不得硬编码 Linux 的 Playwright 路径。当前没有 Windows 10 实机条件时，性能和 UI 验收只登记 Windows 11 x64 结果，并明确标注 Windows 10 未验证。

## 4. 全量验证触发条件

全量验证不是日常反馈工具。只有建立或重建基线、完成大模块或阶段性里程碑、修改跨模块公共契约/全局 CSS 令牌/bridge 协议/外壳生命周期，或者准备提交/发布时才运行。每次全量验证必须登记触发原因。测试失败时先重跑失败测试及其直接依赖，确认原因后再扩大范围；不得无分析地重复全量测试。

## 5. 进程和临时资源清理

测试启动的服务器、浏览器、Node/.NET 进程和临时文件由当前批次负责清理。脚本必须使用 `try/finally` 关闭服务器和浏览器；批次记录启动的进程 ID、端口和临时目录，并在结束时确认它们已经退出或删除。只允许停止当前批次启动的进程，不得停止用户原有的 PyCharm、其他 agent 或其他应用进程。

## 6. 提交登记

每个大模块形成可独立复验的结果后单独提交，使用 Conventional Commits，例如 `feat(branches): align branch panel actions`。提交前执行 `git diff --cached --check` 和 `git status --short`，只暂存当前批次自己的文件。未验证的产品逻辑、临时桩、无关文件和其他 agent 的改动不得提交。
