# Open Workspace 参考采集（2026-10-19）

## 1. 结论

实际端从工作区 chip 的 `Open Workspace...` 打开 `Open Workspace` 对话框；参考端无项目启动取得 PyCharm 的 `Welcome to PyCharm` 屏幕。两者都提供“最近项目/工作区 + 打开 + Clone Repository”入口，登记为**语义状态参考**，但**不增加页面级 49**：

- 实际是已有工作区上的模态对话框，参考是无项目 Welcome 屏幕；上下文和结构不同。
- 实际没有“新建项目/New Script/New Notebook/Plugins”，参考也没有实际端的“Choose Directory”同名字段；不能宣称严格同态。
- 像素合格仍 **0**。

## 2. 实际端

- 正常入口 PID 20100，隔离夹具 `D:/github/Augit/.tmp/open-workspace-2026-10-19/fixture`，2880×1716、DPI 168、浅色。
- 入口：工作区 chip → `Open Workspace...`。
- 对话框 `Open Workspace`：
  - 左栏 `Recent Workspaces`、`Choose Directory...`、`Clone Repository...`；
  - 右侧最近工作区 `fixture` 与路径，说明 `Opening the same directory activates its existing window.`；
  - 底部 `Cancel`；当前状态没有可用的 `Open` 主按钮。
- 证据：`actual/actual-open-workspace.png`（`F2A3AAE4…AF2C`）。

## 3. 参考端

- PyCharm 2026.2.1 / PY-262.9437.214，PID 14564；无项目启动，显示 `WelcomeScreen`。
- 左栏 `Projects and Files`：`Open...`、`New...`、`Clone Repository...`、`Search projects`，最近项目 `notes.txt` / `D:\JetBrains\PyCharm-2026.2.1-reference\fixture` / `main`。
- 右栏 `Welcome to PyCharm`：`New Script`、`New Notebook`、`Open File`、`Learn`、`Plugins`；底部 Theme `Islands Light`、Keymap `Intellij (Windows)`。
- 证据：`reference/reference-welcome.png`（`117D9502…0307`）。

## 4. 差异与边界

- 实际是“切换工作区”对话框，参考是首次/无项目 Welcome；两者不是同一窗口形态。
- 参考的 New Script/New Notebook/Learn/Plugins、主题/Keymap 属于 PyCharm 产品能力，不新增到 Augit。
- 实际 `Choose Directory...` 与参考 `Open...` 语义相近但字段/流程不同。
- 只覆盖浅色、DPI 168、单次打开；未验证实际选择目录/克隆动作，也未验证参考 Project 打开动作。
- 不把本记录算作页面级配对；`打开工作区` 仍需要同上下文的实际/参考状态才能进入页面计数。

## 5. 清理

- 实际 PID 20100、参考 PID 14564 正常关闭。
- 临时夹具删除；参考 `trusted-paths.xml`、`recentProjects.xml`、`window.state.xml` 已恢复到启动前快照并哈希一致。
- 全局设置仍 `B8868855…DC3D`；未修改代码、未提交、未推送。
- `cleanup-check-170.json`。
