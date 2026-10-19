# Stash Manager 页面级同态配对（2026-10-19）

## 1. 结论

同一隔离夹具、同一条 stash（`stash@{0}: On main: test stash manager`，1 个文件 `notes.txt`）下，两端都显示了安全的 stash 列表与 Apply/Pop 入口：

- 实际：独立 `Stash` 对话框，标题 `Stash Manager`；左列 `stash@{0} test stash manager`；右侧 `stash@{0} · test stash manager`、`main · 2026/10/19 20:19`、`Apply` / `Pop` / `View Content` / `Delete`、`Contains 1 files` + `notes.txt`、`Close`；工具栏 `+`/删除/刷新。
- 参考：Git 工具窗口 `Commit` / `Stash` 标签，Stash 标签激活；列表 `test stash manager 2026/10/19 20:19` / `main`；下方 `1 file` 树 `notes.txt`；`Apply` / `Pop` / 帮助。
- 登记为**页面级同态状态配对（已知差异，非像素合格）**。页面级 **49 → 50**，像素合格仍 **0**。

此前“参考未找到 Stash Manager”的结论已更正：入口不是 `Stashes`/`Stash Manager` 文字，而是 Find Action 的 **`Unstash Changes...`**（`Git.Unstash`），执行后打开 `Stash` 工具标签。

## 2. 夹具与路径

- 隔离夹具：`.tmp/stash-manager-2026-10-19/fixture`，基线 `840fd48 test: stash manager baseline`。
- 修改 `notes.txt` 后 `git stash push -m 'test stash manager'`，得到 `stash@{0}: On main: test stash manager`；工作区干净。
- 实际：正常入口 PID 14568；hamburger → `Git` → `Stashes...`。
- 参考：PyCharm 2026.2.1 / PY-262.9437.214，PID 15680；`Ctrl+Shift+A` → `Unstash Changes...` → Enter；参考窗口最大化到 2880×1716。

## 3. 对照

| 项目 | 实际 | 参考 |
| --- | --- | --- |
| 容器 | 独立 `Stash` 对话框 | Git 工具窗口 `Stash` 标签 |
| stash 列表 | `stash@{0} test stash manager` | `test stash manager 2026/10/19 20:19` |
| 分支 | `main` | `main` |
| 文件 | `Contains 1 files` + `notes.txt` | `1 file` 树 + `notes.txt` |
| 动作 | Apply / Pop / View Content / Delete | Apply / Pop |
| 其他 | 工具栏 +/删除/刷新、Close | Commit/Log/Console 同窗、帮助 |

已知差异：实际是模态对话框，参考是工具窗标签；实际多 `View Content`、`Delete`、`+`、刷新、`Close`，参考多 `Commit`/`Log`/`Console` 同窗与帮助；标题、尺寸、列表列/日期呈现不同。不据此判缺陷。

## 4. 边界

- 只验证列表/选择/动作入口，不执行 Apply、Pop、View Content 或 Delete；stash 仍在、工作区未改。
- 只覆盖浅色、DPI 168、单 stash、单文件；未覆盖多 stash、空态、冲突恢复、深色。
- 页面级计数增加 1，是因为两端都到达同语义状态；不是把旧取证改名为通过。像素合格仍 0。

## 5. 证据

- 实际：`artifacts/pixel-followup-2026-10-19/stash-manager-closure/actual/actual-stash-manager.png`（`C4113682…BCD0`）。
- 参考：`reference/reference-stash-manager-max.png`（`143443F2…52CC`），2880×1716、DPI 168。
- 参考入口：`reference/reference-find-unstash.png`（Find Action `Unstash Changes...`）。

## 6. 清理

- 实际 PID 14568、参考 PID 15680 正常关闭；stash 保持 `stash@{0}`，未执行 Apply/Pop/Delete。
- 临时夹具删除；参考 `trusted-paths.xml`、`recentProjects.xml`、`window.state.xml` 从启动前快照恢复并哈希一致。
- 全局设置仍 `B8868855…DC3D`；未修改代码、未提交、未推送。
- `cleanup-check-171.json`。
