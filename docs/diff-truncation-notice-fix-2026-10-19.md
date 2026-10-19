# Diff 2000 行截断提示修复（2026-10-19）

## 1. 根因

宿主 `ShellBridge.cs` 对超大补丁只回传前 **2000** 行，并设置 `truncated=true`：

```csharp
const int maximumRows = 2000;
bool truncated = rows.Count > maximumRows;
if (truncated) rows = rows.Take(maximumRows).ToArray();
```

但旧 UI 从未读取 `diff.truncated`。8,740,031 字节 / 92,001 行 `large.txt` 的改动在最后一行时：

- 实际：正文只渲染前 2000 行，工具栏仍显示 **`0 differences`**，文件末尾改动完全不可见，也没有截断说明。
- 参考 PyCharm 2026.2.1：同一文件完整显示 **`1 difference`**，可滚到第 92001 行看到 `changed line for partial diff`。

这不是参考同态；实际有 2000 行实现上限，但必须明确告知用户，不能把“前 2000 行无差异”说成“0 differences”。

## 2. 修复

- `web/src/mockup.js` / `docs/ux-mockups/mockup.js`：
  - 新增 `liveDiffTruncationNotice()`，仅当 `diff.truncated === true` 时显示：
    `Diff truncated` + `Showing the first 2000 rows. Changes in the rest of the file are not shown in this comparison.`
  - 截断时工具栏摘要改为 `Truncated`，不再显示不完整计数。
- `web/src/mockup.css` / `docs/ux-mockups/mockup.css`：
  - `.diff-truncated-notice` 使用既有 `.inline-alert`；
  - 给 `.diff-layout:has(> .diff-truncated-notice)` 显式增加 `auto` 行，避免说明与正文同占第 3 行重叠；
  - unified 模式同样增加行。

## 3. 验证

- 正常入口 PID 13488，8,740,031 字节 / 92,001 行文件：
  - 工具栏 `Truncated`；
  - 正文顶部一条黄色 `Diff truncated` 说明，位于文件栏与正文之间，不覆盖行；
  - 正文显示前 2000 行，滚动/计数不再被误读为完整 0 differences。
- 参考 PID 9844 完整显示 `1 difference` 与第 92001 行。
- 新增 `tools/test-diff-truncation-notice.mjs`：**5/5 PASS**。
- `verify-ui-assets.ps1` PASS；`DOC_CLAIMS_OK`；`git diff --check` 通过。

## 4. 计数与边界

- 这是实现上限的可信提示修复，不是参考同态；页面级仍 **50**、像素仍 **0**。
- `Diff 局部加载` 仍需真正的慢读/骨架/重试状态；本项只保证 2000 行截断不再被误报为 0 differences。
- 参考没有该 2000 行上限，实际保留该上限的产品选择未改变。

## 5. 证据

- 修复前：`actual/actual-diff-late.png`（`60020020…E73C`，`0 differences`）。
- 修复后：`actual/actual-diff-truncated-notice2.png`（`26DE0B15…9226`，`Truncated` + 说明）。
- 参考：`reference/reference-diff-late.png`（`B0A8709D…1EF3`，`1 difference` / 第 92001 行）。

## 6. 身份与清理

- 新 `mockup.js` `265CD54C…D788`；新 `mockup.css` `037517C2…4EF9`；`live-data.js` 仍 `12D57E34…1B3D`。
- 实际 PID 13488、参考 PID 9844 正常关闭；临时夹具删除；参考配置恢复；全局设置仍 `B8868855…DC3D`。
- 未提交、未推送；`cleanup-check-175.json`。
