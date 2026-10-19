# Diff 上限参考态补齐（2026-10-19）

## 1. 结论

同一份 25,380,029 字节、270,000 行的 `big.txt`（提交 `70a24f0 test: create big diff fixture`，工作区 `M big.txt`）在两端打开 Commit Diff 后，**都拒绝显示 diff 正文**，登记为 **Diff 上限状态级同态配对**：

| 项目 | 实际 | 参考 |
| --- | --- | --- |
| 页面 | `Commit: big.txt` | `Commit: big.txt` |
| 状态 | `Text diff is unavailable for this file (SideTooLarge).` | `Cannot show diff: Cannot show the contents of '…big.txt'. The file is bigger than 20.97 MB.` |
| 正文 | 无 diff 正文 | 无 diff 正文 |
| 附加动作 | `Close this comparison` / `Open in its default application` 的文字说明 | `Reload` |
| 阈值文案 | `SideTooLarge` | `20.97 MB` |
| 其他 | 同一提交、同一文件、1 个修改文件 | 同一提交、同一文件、1 个修改文件 |

**没有增加页面级配对**：页面级仍 **48**，像素合格仍 **0**。本项补的是“Diff 局部加载及上限参考态”中的**上限状态**；`Diff 局部加载` 页面仍未取得，不能用本状态冒充。

## 2. 方法

- 隔离夹具：`D:/github/Augit/.tmp/diff-limit-2026-10-19/fixture`，Git 仅在此夹具内写入；基线提交 `70a24f0`，随后追加一行形成 25,380,029 字节的工作区修改。夹具 SHA256 `803119BB…EF4E`。
- 实际：正常入口 `tools/start-augit.ps1 -Workspace … -SettingsFile …`，PID 17436；点击 Commit 轨道 → 双击 `big.txt` → `Commit: big.txt`。
- 参考：`D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe`，`reference-2026.2.1.properties`，PID 20360 / 12384；`Ctrl+K` 打开 Commit → 单击 `big.txt` → `Ctrl+D` 打开 Diff。
- 参考前一次直接双击文件行只切换/折叠节点，未打开 Diff；改用“选中行 + Ctrl+D”后成功。该失败是**操作路径**问题，不是参考产品没有该状态。

## 3. 差异与边界

- 阈值不同：参考明确显示 `bigger than 20.97 MB`；实际只显示 `SideTooLarge`，没有在 UI 中给出数值阈值。不能用本证据反推实际阈值等于 20.97 MB。
- 动作不同：参考有 `Reload`；实际没有 Reload，只有关闭比较/用默认程序打开的文字说明。该差异按现有产品范围登记，不据此判缺陷。
- 参考的完整提示由本地 `VcsBundle.properties` 的 `file.content.too.big.to.load.increase.property.suggestion` 定义，建议增加 `idea.properties` 中的 `idea.max.vcs.loaded.size.kb`；截图中右侧文字被面板宽度截断，文档不把被截断部分当作屏幕可见证据。
- 只覆盖浅色、DPI 168、25.38 MB 文本、Commit 视图的单文件 Diff；未覆盖 `OutputTooLarge`、二进制 Diff、图片 Diff、局部加载/重试、深色、其他 DPI。
- 本项是**状态级**；不增加页面级 48，也不表示 54 页的 Diff 局部加载已完成。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-19/diff-limit-closure/actual/actual-diff-limit.png`（SHA256 `472693A5…021A`）。
- 参考：`artifacts/pixel-followup-2026-10-19/diff-limit-closure/reference/reference-25mb-diff-max.png`（SHA256 `8D6ABB1E…3A28`），2880×1716、DPI 168。
- 清单：`artifacts/pixel-followup-2026-10-19/diff-limit-closure/evidence-manifest.json`。

## 5. 清理

- 实际 PID 17436 正常关闭；参考 PID 20360 / 12384 经 Confirm Exit 关闭。
- 临时夹具与隔离设置目录已删除；参考 `trusted-paths.xml` 已恢复为启动前快照 `6E40F668…D2C9`，`recentProjects.xml` 移除了本轮临时项目并把 `lastOpenedProject` 恢复为参考 fixture，`window.state.xml` 恢复参考 fixture 的窗口尺寸；未覆盖用户现有信任列表或主题/字体。
- 实际全局设置未变，仍 `B8868855…DC3D`；未修改代码、未提交、未推送。
- `cleanup-check-166.json`。
