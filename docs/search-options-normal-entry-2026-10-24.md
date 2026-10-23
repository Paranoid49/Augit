# 搜索选项与深色 / 窄窗正常入口（2026-10-24）

> 结论：在真实外壳、真实 `Ctrl+Shift+F` / 剪贴板输入 / Win32 点击下，Match case、Words、
> Regex 都重跑并改变结果；**Include ignored files 原先只切换 checkbox 视觉状态、不重跑查询**，
> 修复后勾选/取消都会重跑。深色和窄窗截图已取得；参考整页像素仍未验收。

## 1. 夹具与方法

- 临时仓库：`src/alpha.txt`（`Alpha alpha ALPHA alphabet` / `second Alpha line`）、
  `src/beta.txt`（`beta Alpha`）、`ignored/build.txt`（`alpha ignored`）、`.gitignore` 忽略 `ignored/`；
  已提交。
- 隔离设置主题改为 `Dark`；真实外壳 `-SkipBuild`，Light 以外的深色界面由设置决定。
- 真实入口：主窗 topmost，单击内容，`keybd_event` 发 `Ctrl+Shift+F`；剪贴板粘贴
  `alpha` / `alph` / `^Alpha`；选项和 checkbox 用 `click-window.ps1` 真实点击。
- CDP 只读结果数、`aria-pressed`、checkbox、浮层/视口几何并截图。
- 窄窗：`SetWindowPos` 改到 **1024×640 CSS 视口**（980×760 物理请求，应用实际客户区）。

## 2. 结果

| 状态 | 查询 | 选项 | 结果 |
| --- | --- | --- | --- |
| 基线 | `alpha` | 全部关闭 | 3 |
| Match case 开 | `alpha` | case | 1 |
| Match case 关 | `alpha` | — | 3 |
| Include ignored 勾选 | `alpha` | ignored | **4**（增加 `ignored/build.txt`） |
| Include ignored 取消 | `alpha` | — | 3 |
| Words 前 | `alph` | — | 3 |
| Words 开 | `alph` | words | **0** |
| Words 关 | `alph` | — | 3 |
| Regex 前 | `^Alpha` | — | 0（按字面） |
| Regex 开 | `^Alpha` | regex | **1**（第一行 `Alpha`；第二行以 `second` 开头） |
| Regex 关 | `^Alpha` | — | 0 |
| 窄窗 | `^Alpha` | 全部关闭 | 0；视口 1024×640，浮层 730×202，x=147，未越界 |

- 深色截图：`artifacts/search-options-normal-entry-2026-10-24d/dark-baseline.png`、
  `dark-ignored.png`、`dark-words.png`、`dark-regex.png`、`dark-narrow.png`。
- 报告：`search-options-normal-entry.json`（每个状态的计数、`pressed`、checkbox、浮层/视口几何）。
- 实例 PID 20596 正常关闭，`pidAlive=false`；临时仓库删除。

## 3. 发现与修复：Include ignored files 不生效

- 修复前：勾选 checkbox 后 `checked=true`，但结果仍为 3；宿主请求未带
  `includeIgnoredFiles=true`。原因：`live-data.js` 只为 `.search-option` 按钮绑定了重跑查询，
  `.search-ignored input` 没有 `change` 监听。
- 修复：新增 `change` 事件委托；状态落地后读取 `currentSearchOptions()` 重跑同一查询。
- 修复后正常入口：勾选 3→4（出现 `ignored/build.txt`），取消 4→3；查询和浮层保持。
- 自动化：`live-shell` 的 `repository-search` 场景补断言——stub 在
  `includeIgnoredFiles=true` 时返回一条额外忽略文件命中；检查勾选/取消各触发一次重跑、
  结果数 +1/−1、`ignored/build.txt` 出现。

## 4. 边界

- 深色与窄窗只覆盖当前 DPI、单屏、一个 1024×640 视口；未测极窄宽度换行、浅色对照和
  参考整页像素。
- Include ignored 的语义验证使用本地 `.gitignore` 和一条被忽略文件；不代表所有 ignore
  规则（嵌套 `.gitignore`、global excludes、symlink）都已覆盖。
- `Words`、`Regex` 的计数由小夹具给出；没有对参考 PyCharm 的搜索选项状态做逐像素配对。
- 本轮修复的是 checkbox 未重跑查询，不改变搜索结果的排序、限制或错误文案。

## 5. 复验

```powershell
node tools/audit/search-options-normal-entry.cjs `
  C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright `
  artifacts/search-options-normal-entry-2026-10-24d
node tools/audit/live-shell.spec.cjs <playwright>
```

- 正常入口脚本生成 5 张深色截图、选项状态 JSON，关闭实例并删除临时仓库。
