# Rollback 树展开箭头行内缩进整改（2026-11-28）

## 1. 用户报告

Rollback Changes 对话框里，树的展开箭头全部贴在对话框最左侧排成一列，复选框、目录图标和名称却在各自缩进位置；红框里看到的是“箭头梯子”，和 PyCharm 参考截图（箭头紧挨各层行的复选框）不一致。

## 2. 根因

`web/src/mockup.css` 里有一条全局规则：

```css
.change-chevron { position: absolute; left: 0; ... }
```

它原本只服务 Changes 分组头的行首箭头，但选择器没有限定容器，Rollback 树也使用了 `.change-chevron`。Rollback 行没有定位上下文，所有层的箭头都被绝对定位到对话框左边；同时 flex 布局仍为箭头预留了 16px，导致每行的复选框/图标继续缩进，视觉上箭头和内容脱节。

## 3. 修复

- 把绝对定位规则限定到 Changes 分组行：`.changes-list .check-group-row > .change-chevron`。
- Rollback 行内箭头显式 `position: static; left: auto`，继续作为 flex 的第一列按 `--rollback-depth` 缩进。
- 文件行的 `.rollback-chevron-spacer` 仍保留 16px 占位，根/目录/文件三级复选框对齐。
- 审计 `.change-chevron` 的全部使用点：只有 Changes 分组头和 Rollback 树；其他历史/Blame/弹层没有同类绝对定位。

## 4. 验证

- 新增 live-shell 断言：Rollback 根箭头和目录箭头 computed `position` 为 `static`；目录箭头比根箭头向右超过 10px；两个箭头都落在各自行矩形内。定向 Rollback 块 **19/19 PASS**。
- 修复后截图：`artifacts/rollback-tree-arrow-alignment-2026-11-28/after.png`（SHA-256 `26CF93F9…`）；箭头在 `live-ws` → `notes`/`src` 的每一级与复选框/目录图标对齐。
- 受影响范围仅为 Rollback 树箭头布局；Changes 分组头行的绝对箭头行为保持不变，树勾选、展开/折叠和回滚动作未改。
- 临时测试文件、截图和进程已清理。
