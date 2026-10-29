# Augit 品牌徽标

Augit 使用自有徽标，不再用纯文字 `A` 充当应用图标。

## 图形

- 蓝底圆角方形，左上到右下的蓝色渐变；
- 中央白色无衬线 A 字标，三角形字怀；
- 内圈 2px 半透明白色描边，保证在任务栏小尺寸下仍有轮廓。

## 资产

| 用途 | 路径 |
| --- | --- |
| SVG 源/网页 favicon | `web/assets/augit.svg` |
| 标题栏 `.brand-mark` | `web/src/mockup.css` 内嵌同一 SVG 的 data URI |
| Windows 应用/任务栏图标 | `src/Augit.Shell/Assets/augit.ico`（16/20/24/32/40/48/64/128/256） |
| 安装器向导图标 | `tools/packaging/Augit.iss` 的 `SetupIconFile` |

## 接线

- `Augit.Shell.csproj` 的 `<ApplicationIcon>` 把 ICO 嵌入 exe 资源；
- `ShellWindow` 用资源 ID `32512`（`LoadImage`/`LoadIcon`）设置窗口类和 `WM_SETICON`，
  覆盖任务栏、Alt-Tab 和窗口图标；
- 标题栏、工作区 chip 和对话框标题的 `.brand-mark` 使用同一 SVG；
- `web/index.html`、`web/settings-surface.html` 的 favicon 指向该 SVG。