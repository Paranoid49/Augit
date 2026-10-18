# PyCharm 参考外观整改记录（2026-10-09）

本轮按用户列出的优先顺序完成可执行整改，并用当前 Windows 11 x64、175% DPI 的 PyCharm Islands Light／Dark 场景和 IntelliJ 源码进行复核。该记录不把产品范围差异或未裁决规范冲突写成已完成的视觉等价。

## 生效基线

- Augit 实机生效界面字体为 `Microsoft YaHei UI` 12px，等宽字体为 `Cascadia Mono` 13px；界面语言为英文，DPI 为 175%。
- 项目颜色使用固定持久索引，当前工作区索引为 4；路径大小写和分隔符规范化后重载保持不变。
- 当前 PyCharm 取证附件：`artifacts/reference-restoration-2026-10-09/pycharm-light-current.png`、`pycharm-dark-current.png`、`shell-reference-current.png` 和 `shell-reference-current.json`。
- PyCharm 浅色状态栏空白采样为 `#E9EAEE`；深色安装版采样为 `#27282C`，源码基准仍记录为 `#26282C`，没有用安装版单点样本覆盖源码令牌。

## 逐项结果

| 编号 | 结果 | 当前实现或边界 |
| --- | --- | --- |
| 1 | 已整改 | 真实运行配置已恢复为 Microsoft YaHei UI 12px；恢复设置会保存界面与等宽字体的独立值。 |
| 2 | 已整改 | 主界面固定英文，中文用户数据、文件正文、提交信息、分支名和原始 Git 输出保持原文；设置页在 12px、旧 Segoe UI 14px 和 40px 下通过无横向溢出检查。 |
| 3 | 已整改 | 项目颜色改为持久索引，不再按工作区路径哈希；首次工作区使用 IntelliJ 蓝色索引，重载保持颜色。 |
| 4 | 已整改并有实测附件 | 标签栏为 40px，标签卡片为 28px；选中标签使用圆角卡片、描边和关闭叉。中文标签宽度使用实际字体测量并由省略规则约束。 |
| 5 | 已整改 | Changes、历史和分支树的文件图标与文字间距统一为 2px；项目树与通用树行分别复核。 |
| 6 | 已整改并自动验证 | 工具轨的 hover、pressed、visible、active、focus、disabled 组合按状态优先级验证，disabled 清除过期背景和焦点墨迹。 |
| 7 | 已整改并自动验证 | 项目、提交、Git 历史、终端、搜索、定位、历史、折叠、复制和文件类型图标均与本地 IntelliJ SVG 逐主题栅格比对；浅深主题共 86 项通过。 |
| 8 | 已整改 | YAML 保留纸张、折角和 Y 字形多层结构，增加专用 Y 颜色令牌；深色通过主题颜色补丁处理，不分发官方资源。 |
| 9 | 产品边界差异 | Augit 状态栏继续显示路径、编码、换行和只读；PyCharm 的行列、缩进和解释器字段不属于当前产品范围。颜色已按当前浅色实测采样记录。 |
| 10 | 已整改并验证基础密度 | Git 工具窗保留提交行 26px、筛选栏、作者/时间列、引用树、详情区和空态布局；实时布局套件覆盖 891 项，未将产品字段差异伪装成 PyCharm 功能。 |
| 11 | 已校准 | 状态栏颜色取当前浅色附件 `#E9EAEE`；深色源码与安装版存在 1 个通道的环境差异，保留为证据说明而不擅自改令牌。 |
| 12 | 有意实现差异 | WebView2 使用区域级 CSS 细滚动条，普通区域与标签区域分别提供普通、hover 颜色；动画、自动隐藏和拖动手感仍不是 Swing 原生滚动条等价物。 |
| 13 | 已覆盖主框架 | Islands Dark 主窗、工具轨、弹层、正文和状态色已按同一场景验证；未把浅色证据外推为深色全页面逐像素完成。 |
| 14 | 等待裁决 | UX 规范同时存在“Enter 临时预览”和“Enter 正式标签”，且结果上限存在 30／100 两处约束。当前没有改动该状态机，等待用户选择最终规则。 |
| 15 | 产品边界差异 | Augit 的快速搜索保持单一文件搜索浮层，不增加 PyCharm 的 All／Classes／Files／Symbols／Actions／Text 分类页签。 |
| 16 | 产品边界差异 | 右键菜单保留产品范围内的 Diff、回滚、历史、复制和定位动作，不新增重构、移动和安全删除。高度与密度按现有动作集合适配。 |
| 17 | 已补充说明 | `indexStatus` 与 `workTreeStatus` 同时写入文件行的 aria-label、title 和读屏说明，覆盖 MM、AM、D? 与仅工作区修改；未增加第二个常驻颜色列。 |
| 18 | 已整改 | 运行时使用显式中文字体族，避免 Segoe UI 中文回退导致的字形基线和密度漂移；恢复路径和 UI 文案测试覆盖。 |
| 19 | 已验证 | 设置页在中文用户数据、14px 旧配置和 40px 大字号下检查字段裁切与横向溢出；界面固定英文不再由中文标签撑宽布局。 |

## 验证结果

- `live-shell.spec.cjs --reference-only`：10 项通过。
- `live-shell.spec.cjs --restoration-only`：23 项通过。
- `live-shell.spec.cjs --layout-only`：891 项通过。
- `live-shell.spec.cjs --icons-only`：86 项通过，包含浅深主题搜索工具轨和分支图标。
- `verify-ui-assets.ps1`、JavaScript 语法检查和 `git diff --check`：通过。
- `dotnet build Augit.slnx --no-restore --disable-build-servers -c Release`：0 警告、0 错误。
- Windows 11 x64 已取证；没有 Windows 10 22H2 实机，不宣称 Windows 10 验收完成。

完整实时套件已运行到边界导航、设置、查找、状态栏、Blame、终端和字号矩阵等后段；因本轮收口和资源清理要求未继续跑到最终汇总，不能计为全量通过。中途通过的阶段结果不替代全量结果。浏览器、测试服务器和本轮启动的 Augit 实例均已清理，保留的 reference-restoration 图片和 JSON 是正式验收附件。

## 手动运行与停止

```powershell
$augitPreview = Start-Process -FilePath 'D:/github/Augit/src/Augit.Shell/bin/Release/net10.0-windows/win-x64/Augit.exe' -ArgumentList '--workspace D:/github/Augit --web-root D:/github/Augit/web --width 1646 --height 981 --no-session-restore' -PassThru

$augitPreview.Refresh()
if (-not $augitPreview.HasExited) {
    [void]$augitPreview.CloseMainWindow()
    if (-not $augitPreview.WaitForExit(5000)) { taskkill /PID $augitPreview.Id /T /F }
}
```
