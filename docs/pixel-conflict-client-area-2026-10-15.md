# 冲突客户区与底栏整改

日期2026-10-15，分支dsh、HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。继续用户批准的PyCharm实机重排，不改变冲突编辑、接受、撤销、取消或保存契约。Windows 10排除本次验证，跨屏DPI未验证。完整55页及模态、背景刷新、终端逐行和完整阴影目标仍未完成。没有暂存、提交或回滚混合工作区。

## 依据与修改

前批reference-runtime-geometry.json来自实际PyCharm 262.9437.214：根面板988×694逻辑像素、Insets全0，标题40、工具栏42、来源标题30（已含边框），底栏50。底栏Insets上下8、左右12；按钮高34，同组间距6，Accept Left/Right分别99/108、Apply/Cancel分别120/78。尺寸是参考实际逻辑坐标，不直接等于DWM截图框尺寸除以DPR，仍存在设备空间相位问题。

本地DarculaButtonUI.getDarculaButtonSize证明按钮含透明边框，横向内留白14×2；MergeRequestProcessor.updateBottomActions使用UIUtil.PANEL_REGULAR_INSETS。之前实现漏算命中边框，左右接受强制等宽、底栏上下不对称；冲突窗口外框还压缩了内容宽度，来源标题额外1px补偿掩盖了实际层级。

共享mockup.js/CSS现改：

- 按钮高度max(34,字体行高+14)，独立按文案宽度+34、下限78定宽。
- 底栏上下8、左右12，同组间距6；单层高度按钮+16，换行高度两倍按钮+24。
- 冲突外框使用不参与布局的伪元素描边，不改变原生外壳技术与阴影。
- 工具栏默认42，来源标题默认30且已包含边框；取消额外1px。保持布局居中及设备像素SVG横线，未增加文字平移。

同步design-system.md与ux-spec.md。大字号适配只由自动化验证，不宣称参考实测一致。

## 原图和量测

所有路径相对于artifacts/pixel-followup-2026-10-15。参考仍为conflict-reference-clean/reference-{initial,left,resolved}-clear.png，不重采样、不平移。conflict-footer-screen为只改底栏后的中间证据；conflict-client-frame为本批最终系统原图，1730×1215、DPI168。初始、左侧接受、全部处理均采集成功；最终初始图已目视。

最终三状态完整不同像素83154/89632/99240，正文27296/27920/31174，固定横线[2,194,1728,196]差异均0。初始完整差异较前批conflict-integer-split的93952减少，但不能只用总量证明所有区域改善。

比较器新增固定底栏区域y1120至底边（含过渡带）：初始不同像素从23838，经底栏中间态18684，最终12142；最终左侧接受17996、全部处理24258。中央x1000的panel→底栏背景切换最终为y1127，与参考一致，前批为1129。

仍有明确差异：最终左栏文字相对参考左2物理px，中央末端左2、右栏左1，第一/第三行顶部高1；右栏第二行也高1。参考根面板逻辑坐标与屏幕裁框存在分数相位，不能继续依靠固定像素偏移掩盖。来源标题、工具图标、按钮绘制、字体抗锯齿、边缘圆角仍不同。旧pair门禁中的实际字形回退证据缺口没有消除；完整同态计数不增加。

## 验证

- 改按钮后72组布局通过；后续外框/标题变更影响布局，最终72组重验通过。新增独立按钮宽度、8/12留白、6px间距、外框不占位断言。
- 60组横线栅格通过，覆盖五DPR、双主题、UI12/40及初始/焦点/失焦；断言更新为参考实际30px标题。
- 真实深色多块滚动、横滚槽固定、视口外焦点及精确保存通过，conflict-client-save/multi.json的exactSaved=true。
- 比较器2项测试通过，新增1119/1120边界单像素验证，确认底栏区域计数不混淆。
- 收尾共享9资产一致；受影响3个JavaScript文件语法、文档声明及git diff --check通过。源文件与记录为UTF-8/LF；历史全量快照过期，未作本批依据。
- 未运行产品全量测试、51项冲突全套或C#构建。

产品MD5：mockup.js从35E2450D6BEA6B5FE475D2BD3657ABC2变为56AF172CFF9BBD45FDFDFE5F9003143B；mockup.css从80F00477A5FEC1D0BEACD0048863A289变为F9F29FAB9339BD2420A216408F2E00D7。live-data.js仍为00E7ECB134CBD0939313286BE7C92E38，bridge.js仍为F297BF8769E249CA0D804F4F4EEEF5A9。

## 进程与手动复验

本批未启动参考PyCharm。只读复查发现前批PID21368已不存在，无法确定退出原因，不能声称本代理清理成功。前批专用临时目录仍在，历史清理拒绝未重试；目录见前批运行时几何记录。

本批真实外壳PID15200/5280/14000，端口59215/49236/59174，临时目录Augit-merge-pair-3UFScm/9HyJPX/lr0gAu均由驱动finally回收。专项浏览器finally关闭；证据图片与JSON保留为成果。
收尾只读复查上述三个PID不存在，三个端口无监听，三个目录均不存在。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$chrome='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/verify-ux-conflict.cjs $pw $chrome artifacts/conflict-client-layout-manual-new
node tools/audit/verify-conflict-title-separator.cjs $pw $chrome
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-client-screen-manual-new $pw --light --screen-pair --reference-frame --pair-focus
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-client-save-manual-new $pw --multi --reference-frame
# 正常自动清理；异常只停止本次打印的PID，不按进程名批量停止。
taskkill /PID <本次PID> /T /F
python -B -m unittest discover -s tools/audit -p test_clean_conflict.py
python -B tools/audit/compare-clean-conflict.py artifacts/pixel-followup-2026-10-15/conflict-reference-clean artifacts/pixel-followup-2026-10-15/conflict-client-frame
```
