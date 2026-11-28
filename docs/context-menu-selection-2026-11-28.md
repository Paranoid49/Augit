# 右键未选中行的高亮整改（2026-11-28）

## 1. 用户报告

项目树中先选中 `maven-project1`，再在未选中的 `pom.xml` 上右键：菜单正确出现在 `pom.xml`，但原来的 `maven-project1` 仍保持高亮。菜单动作的目标与视觉选中行不一致。

## 2. 参考规则

IntelliJ Platform/Swing 树的右键选择规则（`platform/platform-impl/src/com/intellij/ui/tree/Tree.java:1112-1130`，引用树此前已按此实现）：

- 右键落在**不在选中集里**的行：先把选中集替换为该行；
- 右键落在已选中的行：保持原选择（多选时保持整个选中集）；
- 菜单动作使用被右键的行/选中集。

Augit 的项目树、Changes 列表和 Git 日志都是单选列表，按同一规则处理。

## 3. 实现与审计

| 入口 | 处理 | 结果 |
| --- | --- | --- |
| 项目树文件/目录/根行 | `openTreeContextMenu` 先 `selectTreeRow(row, { follow:false })`、`focusTreeRow`，再打开菜单 | 高亮、`live.treeSelectedPath` 与菜单目标一致；右键只选择，不打开正文/更新已有 Diff |
| Changes 文件行 | `openChangesContextMenu` 先 `selectChangeRow(row, { follow:false })`、聚焦行，再打开菜单 | 高亮、`live.selectedChangePath`、菜单目标一致；不额外更新 Diff；工具栏状态同步 |
| Git 日志提交行 | `openLogContextMenu` 先通过既有选择入口选中提交、聚焦列表，再打开日志菜单 | 选中行、`live.historySelectedHash`、菜单目标一致；提交详情跟随 |
| 引用树分支/标签 | 已有 `openRefTreeMenu` 的选中集规则 | 原本正确；补了右键行焦点 |
| 分支弹层中的引用行 | 弹层没有持久选中集，`.selected` 是当前分支标记；菜单数据自身带目标 | 不属于“旧高亮”问题，保持当前分支标记 |
| Changes 静态视觉稿 | `showMenu` 也先设置目标行 `.selected` | 视觉稿交互一致 |
| 历史/Blame 行 | 历史文件静态菜单原先已先选中；Blame 行没有持久选中列表 | 无需相同修复 |

## 4. 验证

- 新增 live-shell 定向断言 **3/3 PASS**：
  - `项目树右键未选中行先替换高亮并让菜单指向该行`
  - `Changes 右键未选中行先替换高亮并让菜单指向该行`
  - `Git 日志右键未选中提交先替换高亮并让菜单指向该提交`
- 每个断言同时检查 DOM 唯一高亮行、状态字段和菜单数据集三者相等，防止“视觉修了但动作仍取旧行”。
- 受影响范围：项目树、Changes、Git 日志的右键选择链路；未改变单击、双击、复选框、Diff 跟随和菜单动作实现。
- 临时测试进程、浏览器和文件均已清理。
