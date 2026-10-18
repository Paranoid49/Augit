# 同尺寸冲突三栏与来源槽整改

日期：2026-10-15。承接用户已批准的实机布局裁决。整体目标仍未完成；Windows 10不在本次验证范围，跨屏DPI保留未验证。保留混合工作区，无回滚、暂存或提交。

## 本批证据和修改

旧实际窗口1750×1214，参考1730×1215，DPI同为168。驱动新增--reference-frame，从参考原始PNG元数据读取尺寸，通过现有HTML窗口尺寸设置取得1730×1215实际系统PNG；不裁图、缩放或移动图片像素。此模式仅统一审计窗口尺寸，不修改产品默认大小。记录三栏、行号槽、动作和正文物理坐标到layout.json。

参考来源仍为conflict-line-pair/reference-initial.png；初始同尺寸实际在conflict-frame-before。源码ThreeDiffSplitter.java先扣除两个24px连接带再等分，EditorGutterComponentImpl.java按真实最大行号与New UI断点图标下限度量编号，镜像编号单独绘制。

两侧原用三位ch加54px，即本机77.39逻辑px；现在与中央一样按真实最大编号、最低16px度量，两侧加60px动作/留白，默认76px。左正文留白18px，右正文在槽后2px；左动作距槽左18px，右动作距槽右12px；左编号左边界固定54px，右编号距槽右51px。多位编号不再因右对齐挤入左侧动作区。固定留白由本机175%参考图派生，不宣称全部字号的上游常量。中央43px及既有编辑模型不变。

同尺寸原图分析：左侧三行墨迹横边界由实际[24,161]、[25,161]、[24,147]改为[35,171]、[35,171]、[34,157]，与参考对应边界一致。阈值140、三通道暗墨迹，边界右侧不含；具体结果在conflict-frame-gutter/ink-comparison.json。这只证明这些字串横边界，不证明字形像素一致。

三栏墨迹仍统一比参考高2物理px。实际宿主行内基线15.991px，与已测16px目标差约0.009px；隔离Chromium为16px。因此没有加入硬编码基线平移。参考来源底部分隔线y194–195，实际在y192–194附近有抗锯齿，布局和栅格仍需继续核对。另发现来源标题/连接带透出灰色对话框底，参考为编辑器底色，已令conflict-columns使用panel底色。

## 验证与失败

- 行号专项扩展至三栏，24组覆盖浅深、DPR1/1.75、13/40字号、4/120/10001行；检查实际字宽、留白、多位编号和编辑隔离。最终固定左边界后的24组通过。
- 真实深色多块首次失败：新694px高窗口使旧12行间隔的第二块已在视口内，不能证明聚焦滚动。夹具扩展到40行，保持scroll>0强断言；最终聚焦滚动434.86px、纵横滚动节点复用、正文对齐与精确保存通过。
- 最终动作槽三状态系统图在conflict-source-gutter-final，身份/尺寸一致；初始、左侧接受、全部处理草稿及磁盘未写入断言通过。浅色空侧精确保存和Git已解决状态通过，证据在conflict-source-gutter-empty。
- 后续左编号由right14改为left54，默认一位编号位置相同，多位编号经24组重验；最后panel底色改动由布局专项验证，不把较早系统PNG称为最终底色取证。
- 不运行产品全量或未修改的Shell构建。

产品MD5：mockup.js从7ADAD5CBF206BC11D9AF3FC026CFCE76变为69203C667E940D79782FA85B600D49B4；mockup.css从E9CF7BDFEE3AABE2300027CC4672840C经过759B290CD86F45739CE6124EA2B76F19，最终变为5B995560780CC614EEE26CF4AFD68193；live-data.js仍为00E7ECB134CBD0939313286BE7C92E38，bridge.js仍为F297BF8769E249CA0D804F4F4EEEF5A9。

## 启停和边界

实际PID5232/7968/14004/8264/20432/12456均由finally关闭；端口64890/61181/54751/62236/65381/58991复查无监听。临时仓库与profile由驱动清理。没有启动PyCharm；未操作用户既有进程，未重试历史拒绝清理项。诊断脚本保存在tools/audit，截图/JSON是成果。

```powershell
$pw="C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright"
$chrome="C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe"
node tools/audit/verify-conflict-result-gutter.cjs $pw $chrome
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-frame-manual $pw --light --screen-pair --reference-frame
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-multi-manual $pw --multi
# 正常由finally关闭，异常仅使用本次输出PID。
taskkill /PID <本次AUDIT_PID> /T /F
```

参考已解决状态仍有提示遮挡；两端文字、槽位、边框和工具项仍不同。尺寸统一不等于55页完整同态门禁通过；未增加合格配对计数。背景刷新、终端精确行配对、完整系统阴影继续未完成。

## 最终复核补记

72组布局最终通过，包含编辑器底色断言。补拍conflict-source-gutter-complete后，与参考目视发现整栏竖线归属错误；上游EditorGutterComponentImpl的New UI分隔线绘于行号槽与正文交界，已删除列外边框，使用不占布局且不参与命中的槽边缘绘制。左来源镜像，中央/右来源位于右边。24组专项新增边界位置、宽度和命中断言并通过；最终系统三状态在conflict-source-separator-final，1730×1215，已目视初始图。来源标题底色与槽边缘已修正，横纵墨迹、标题文字、工具项及底部按钮仍有差异，不宣称像素验收。

本次新增PID22072/14652，端口53644/64419；驱动finally均退出，临时目录Augit-merge-pair-m3EQgu/ExuPCU已删除。专项浏览器finally关闭。两个临时分析脚本移至tools/audit/compare-conflict-source-ink.py及read-conflict-source-geometry.py，保留可复验来源。共享9资产、受影响JS语法、文档声明及git diff --check通过；历史全量快照仍过期，未重跑全量。
