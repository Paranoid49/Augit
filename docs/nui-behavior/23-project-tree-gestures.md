# 项目树手势分流

本文记录项目树中箭头、目录名称和文件行的手势边界。产品行为以 `docs/product-spec.md` 与 `docs/ux-spec.md` 为准；本地 IntelliJ 源码用于说明参考行为和有意差异。

## 规则

| 目标 | 单击 | 双击 | Enter |
| --- | --- | --- | --- |
| 目录 disclosure 箭头 | 只展开或折叠，不改变选择 | 不适用 | 对焦点目录展开或折叠 |
| 目录名称 | 只选择并聚焦 | 切换一次展开或折叠 | 切换展开或折叠 |
| 文件名称 | 只选择并聚焦 | 打开并激活正式标签 | 打开并激活正式标签 |

单击目录名称不会展开目录；双击只产生一次净切换。三连击的第三次点击不再视为双击，不能把目录切换两次。箭头命中区独立于目录名称，箭头单击不改当前选择。焦点、选中项、展开状态、滚动位置和异步读取请求彼此独立；读取完成或局部重绘不得覆盖用户最后一次选择。

## 依据

- 产品规则：`docs/product-spec.md` §3.1；`docs/ux-spec.md` §7.1。
- 实现：`web/src/live-data.js` 的 `activateTreeRow()`、`hitTreeDisclosure()`、`selectTreeRow()` 和 `restoreTreeState()`；共享模板在 `web/src/mockup.js` 的 `liveTreeRowHtml()`。
- IntelliJ/JDK 对照：`platform/platform-impl/src/com/intellij/ui/tree/ui/DefaultTreeUI.java` 的展开点击计数和命中区；`platform/platform-impl/src/com/intellij/ui/tree/TreeAction.java` 的左右键语义；`platform/platform-api/src/com/intellij/ui/tree/Tree.java` 的默认单击展开设置。

## 差异归类

- 目录名称单击只选择、箭头单击独立展开，是 Augit 产品规则；本地权威默认目录行单击不展开，箭头/名称的事件分区由 Augit 明确实现，归类为“有意产品差异”。
- 文件状态颜色属于文件名节点，类型图标保持类型色；状态映射见 `docs/nui-behavior/08-diff-merge.md` 与 `docs/ux-spec.md` §4.4。暂存后修改的双状态是否增加第二个可见标记，当前规范未定义，归类为“规范未定义或冲突”，不得自行扩展。

## 验证

`tools/audit/live-shell.spec.cjs` 的项目树手势场景覆盖目录名称单击、箭头单击、目录双击、三连击、文件 Enter、`aria-selected`、根路径空字符串焦点恢复和实时 Git 状态标记。没有 Windows 10 22H2 实机条件时，只登记 Windows 11/无头 Chromium 结果，Windows 10 保持未验证。
