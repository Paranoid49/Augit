# 冲突系统截图指针门禁与无遮挡补采

日期2026-10-16。本批接续底栏宽度修复，产品代码未变；上一轮真实宽度校验为进展，本轮解决反复鼠标光晕干扰。

## 采集器修改

capture-screen-region.ps1新增只读GetCursorPos，在屏幕采样前后记录物理坐标；可选PointerClearance默认为-1保持既有调用口径。启用时指针须在采样矩形外扩指定物理像素之外，否则在保存PNG之前拒绝。冲突三状态系统采集固定启用96px保护距，并增加可选pointer-checkpoint用于观察和停放指针。它不移动指针、不更改系统设置，不把保护距通过当成所有遮挡均不存在；unoccluded仍保持null，需要人工审图。

新增test_pointer_clearance.py执行原PowerShell函数，覆盖12个内部、边界、外部及未启用案例；1项通过。原身份诊断3项通过，JS语法、19个PowerShell编码解析及git diff --check通过。没有运行未改动的产品布局或全量回归。

## 失败与成功证据

首次conflict-pointer-gated完成外部窗口指针停放后，直接激活Augit主窗，前景为1051238而目标模态为1507636，门禁正确拒绝保存。没有绕过门禁或将失败算通过。驱动finally清理。

第二次conflict-pointer-final沿用已停放的指针，在主窗进入冲突子窗前完成激活，随后不再切到主窗；三状态全部通过前景/PID/DPI/尺寸及96px指针门禁。初始侧车指针前后均[2397,1377]，采样范围[80,104,1730,1215]，未修改屏幕原图。initial完整原图已目视无光晕，left/resolved的标题及底栏审阅亦无此前光晕。初始结果区首字符焦点、首行、滚动及字体证据沿pairFocus保存。采集不保存夹具结果文件。

三状态完整不同像素为80674/87155/96834，正文25096/25691/28983，底栏11944/17830/24124，标题分隔线均0。仍存在来源标题内容、布局取整、字形和边界差异，不能登记整窗复原通过或增加55页合格计数。

未遮挡Apply区域[1345,1135,1565,1205]在上轮宽度修正后不同像素2305、RGB绝对差387407；此前字重修后宽121时为2651/539949，字重修前为2419/476094。比较不平移、不重采样，证据为primary-button-comparison.json；它证明本局部相对改善，不能外推完整底栏。

## 资源与启停

本批PID2860/2740退出，端口65256/52590无监听，Augit-merge-pair-lBHqm3/hks8sf临时目录均不存在，等待信号由helper finally删除。外部Gemini窗口仅零位移滚动停放指针，没有编辑、提交或发送内容；没有启动或停止用户外部应用。

产品哈希沿底栏宽度批次：mockup.js=44D6C53684B74DBC6116FF25F7C22932，mockup.css=D5DADE8F4DB84506031A9AB3B1F5DB80，live-data.js=C8DA64065C77E60234F2D614334E8DC4，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

取证命令：node tools/audit/capture-conflict-reference-shell.cjs <新输出目录> <Playwright模块路径> --light --screen-pair --reference-frame --pair-focus --activate-main --pointer-checkpoint。实际观察后放行打印的信号文件，不能预先伪造前景确认。驱动finally自动停止；异常仅taskkill /PID <本次打印PID> /T /F。日常启动为powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1，正常关闭窗口退出。

四项整体目标继续保留；本批完成取证条件修复，没有完成背景刷新、55页全模态、终端/冲突整窗或阴影总验收。
