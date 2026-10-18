# 跳转行焦点配色与组件几何接续

日期2026-10-15。上一轮完成日常入口复核，本轮推进55页中的跳转行真实双端比较。仅修复已确证的焦点颜色，几何与整页配对仍未完成；整体目标继续保持active。

## 原图及实际修正

证据目录为 artifacts/pixel-followup-2026-10-15/goto-current-pair。参考预填、参考空输入和实际修前/修后空输入均为584×252系统原始PNG、DPI168，不缩放、不平移。四图已目视检查，窗口完整，无覆盖模态的鼠标光晕。空输入状态聚焦字段；标题/标签保留已授权的功能差异（仅行号与行列号），光标时相并未配准，字体实际回退及全模态状态尚未完成取证，不增加55页合格数。

compact-input的旧蓝色声明以较高特异性覆盖通用focus描边，外环也使用复选框蓝#3574F0；参考边框纯色为#3871E1。本批仅增加跳转行局部focus覆盖，内外描边统一使用accent-brand，视觉稿同步，不扩大到其它输入框。新增焦点颜色断言修前失败（53/116/240而非56/113/225），修后12组通过。

capture-goto-line-shell.cjs移除text-viewer场景参数，增加正常URL断言、字体加载等待和焦点颜色记录。修后通过正常入口取得系统原图，前后前景/进程/边界/DPI稳定，11项既有行为以及新增配色断言通过。

固定输入区域[190,85,562,160]、RGB容差2、区域内排除0像素：不同像素5049→3908，绝对通道差之和1114563→1112940。修前2322个纯复选框蓝像素，修后同位置均为品牌蓝；参考2244个品牌蓝。该局部比较包含光标时相影响，不是完整还原率。蓝框范围仍为参考[203,94,546,147]、实际[203,95,550,151]，几何未在本批修改。

## 下一步所需的权威几何已取得

复用ReferenceMergeGeometry只读premain诊断取得reference-runtime.json。参考实际客户区333×144、标题40高，正文内距8/12/8/12；标签93×34、间距8；输入200×34、四边Insets3，屏幕逻辑位置769/467；两按钮78×34、间距6，位置815/517及899/517。探针仅读取组件，无改布局/内容；Java17编译及既有自检通过。

本地权威DarculaTextBorderNew先扣Insets，DarculaNewUIUtil按focusWidth扩展后绘制even-odd圆角环。参考控件范围与可见蓝框范围不同；当前HTML把可见外框尺寸当成输入布局尺寸，并另加外环。后续应以此模型整改，而不是按截图硬编码平移。本批没有声称几何已修复，也未为通过门禁擅自填补字体回退等条件。

## 验证、哈希与资源

浅深×UI12/40×DPR1/1.75/2共12组定向检查通过；正常入口11项行为及配色断言通过；共享9项资产、两项JS语法和git diff --check通过。Debug/Release构建分别2.81/1.70秒，均0警告0错误。本批无C#逻辑改动，未重复无关全量。

CSS MD5由089ED8741BD87C8A4F95501E3485526E变为D8946B4C1BD29A2DBF5E9190D5D36350；mockup.js=612E9976704343ABE1BB8778B077508F、live-data.js=8A39A72F1D0F8FC7D53D3F158205B2C7、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9不变。未暂存、提交或回滚混合修改。

前三次实际系统采集被门禁拒绝：一次前景仍为PyCharm，两次前景为Augit禁用owner而非模态。Computer Use的Raise提示无缓存动作；标题点击被工具拒绝，因为命中WebView子窗。没有伪造句柄或放宽门禁。停止参考后直接重采成功；成功不能证明失焦根因已修复。

实际PID15684/10024/1772/25664/7304及专用WebView2已退出；端口56842/58852/56715/64371/53495无监听；临时Augit-goto-pair-2fZY0D/845HvF/UlUdK8/R21zpx/SC9ZWR均已独立确认删除。参考PID13308/3912及子树已停止。

自动审批拒绝了读取/停止/删除的组合命令，原因仅为blocked by policy。随后单独停止成功；未绕过删除限制，诊断临时目录仍保留：C:/Users/wl024/AppData/Local/Temp/Augit-goto-geometry-df318a8f92cd49b086f7eca2075d57e8。不能宣称临时文件全部清理。

## 启动与停止

日常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。正常关闭窗口，异常只对本次日志PID执行taskkill /PID <PID> /T /F。

定向复验：node tools/audit/verify-goto-layout.cjs <Playwright模块路径> <Edge可执行路径>；真实取证：node tools/audit/capture-goto-line-shell.cjs <Playwright模块路径> <新输出目录>。驱动finally停止实例并删除自身临时profile，不覆盖旧证据。

55页及全模态、背景刷新、终端/冲突逐行和完整阴影目标仍未完成。Windows10排除本次实机验证，跨屏DPI未验证。
