# 文档相对链接审计（2026-10-18）

## 1. 结论

对 `docs/` 下 265 个 Markdown 文件的相对链接做静态审计：初始 22 个解析失败，修复 5 个后剩余 17 个均为**行号后缀、查询串、外部绝对路径或故意缺失样本**，不是文档断链或产品问题。

## 2. 已修复

- `retained-acceptance-status-2026-10-18.md`：`[终端复制快捷键测试](tools/test-terminal-copy-shortcut.mjs)` → `../tools/test-terminal-copy-shortcut.mjs`（实际文件在仓库根 `tools/`）。
- `design-system.md` §9.3 历史冲突视觉稿：
  - `[首行左侧为空](ux-mockups/conflict-resolver.html?empty-side=left)` 与右侧两枚链接 → 代码文本，并注明“随三栏编辑器 2026-10-17 批准移出范围，文件已删除；仅历史追溯”。
  - 该小节开头已有范围变更说明；`ux-spec.md` §12 场景表同步把 `conflict-resolver` 行改为“历史，文件已删除”，不再指向不存在的文件。
- 三栏冲突编辑器已由用户批准移除；这些页面不是运行时页面或验收目标，历史像素记录不得改写为通过。

## 3. 剩余 17 个解析失败（均非文档断链）

| 类别 | 数量 | 例子 | 说明 |
| --- | --- | --- | --- |
| 带行号/范围的源码引用 | 7 | `../web/src/live-data.js:4038`、`nui-behavior/16-operation-progress.md:57` | 目标文件存在；Markdown 链接把 `:行号` 当作路径，查看器可人工定位 |
| 外部绝对路径/插件缓存 | 2 | `C:/Users/wl024/.codex/plugins/...` | 历史参考工具路径，不属于仓库文档 |
| 故意缺失的样本 | 4 | `assets/missing.png`、`../../secret.png`、`assets/logo.png`、`notes.txt` | `ui-compliance.md` 的链接检查/合规测试样例 |
| 查询串静态页 | 4 | `ux-mockups/text-viewer.html?find-state=loading` 等 | 文件存在；查询串用于状态场景，解析器未剥离 `?` |

## 4. 复验方法

```powershell
$docs=Get-ChildItem 'D:/github/Augit/docs' -Filter '*.md'
$bad=@()
foreach($f in $docs){
  $text=Get-Content $f.FullName -Raw
  foreach($m in [regex]::Matches($text,'\[[^\]]+\]\(([^)]+)\)')){
    $t=$m.Groups[1].Value.Trim()
    if($t -match '^(https?:|#|mailto:|file:)'){ continue }
    $t=$t.Split('#')[0]
    if(-not $t){ continue }
    $p=Join-Path $f.DirectoryName $t
    if(-not (Test-Path -LiteralPath $p)){ $bad += [pscustomobject]@{File=$f.Name;Link=$t} }
  }
}
$bad
```

剩余 17 项应按上表分类人工核对；不把查询串/行号后缀当成断链批量改写。

## 5. 边界

- 本审计只检查 `docs/` 内相对链接，不检查 `artifacts/` 图片文件是否存在、Markdown 锚点或外部 URL。
- 未修改代码、未启动 GUI；`docs/design-system.md`、`docs/ux-spec.md`、`docs/retained-acceptance-status-2026-10-18.md` 为本次文档改动；未提交、未推送。
