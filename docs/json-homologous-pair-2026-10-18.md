# JSON 查看器同态配对（2026-10-18）

## 1. 结论

用同一份 **已展开的 11 行 JSON** 打开实际/参考，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 同一 `sample.json`：`name`/`items`（1,2,3 展开）/`nested.ok`，共 11 行；浅色、2880×1716、DPI168。
- 实际：只读、JSON 格式化视图、无可见语法高亮、文档工具栏；单标签 `sample.json`。
- 参考：可编辑、JSON 语法高亮（键/字符串/数字着色）、`notes`/`image`/`sample.json`/`README` 多标签、状态 `No JSON schema`。
- 行内容与行号一致；已知差异不修。页面级配对 **3 → 4**；像素合格仍 **0**。

## 2. 证据

- 临时 fixture 文件（取证后已删除）：11 行展开 JSON，SHA256 `F103E7F2…0018`。
- 实际：正常入口 PID 11452，项目树打开 `sample.json`；`actual/actual-json-expanded.png`（`de74e0a1…ec1d`）。
- 参考：隔离 2026.2.1，PID 2252，刷新项目树后打开 `sample.json`；`reference/reference-json-expanded.png`（`3c7f30a8…1869`）。
- 对照：可看两份原图；行 1–11 文本一致。

## 3. 已知差异

| 项目 | 实际 | 参考 | 定性 |
| --- | --- | --- | --- |
| 语法高亮 | 无可见着色 | 键/字符串/数字着色 | 产品 JSON 视图差异，未修 |
| 可编辑性 | 只读 | 可编辑 | 产品只读范围 |
| 工具栏 | 文档工具栏/JSON 动作 | 无额外文档工具栏 | 结构 |
| 标签 | 仅 sample.json | notes/image/sample/README | 标签模型 |
| 状态 | 文件信息 | `No JSON schema` | 产品信息差异 |

## 4. 清理

- 实际 PID 11452、参考 PID 2252 正常关闭；fixture 恢复 app.py/notes.txt/README.md，notes.txt 哈希 `DB680844…B612`；设置恢复 `B8868855…DC3D`；未触碰用户原有 PyCharm；无端口/应用残留。
- `cleanup-check-60.json`；未提交、未推送。
