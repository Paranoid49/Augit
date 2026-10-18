# 冲突三状态无遮挡取证接续

日期：2026-10-15。分支dsh，HEAD为3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。承接用户批准的冲突窗口实机整改；Windows 10排除、跨屏DPI未验证。前一批修复光标合成混色，本批完善真实对照条件，没有修改产品行为或用固定偏移修补未知根因。整体目标仍未完成。

## 参考状态与证据

证据目录为artifacts/pixel-followup-2026-10-15/conflict-reference-clean。参考实际product-info为PyCharm 2026.2.1、262.9437.214，独立reference.properties。使用新副本ours/base/theirs/result四文件，从CLI merge入口打开。正文均为first line、base/ours/theirs value、last line及末尾LF。result未保存，结束后仍与base的SHA256相同：14F6DB9E5F72A87AFEB79DFF24E93B5C9E4C457CFA060134CB7C58B977B51CF5。

reference-initial-clear.png、reference-left-clear.png、reference-resolved-clear.png为三份1730×1215、DPI168的原始系统PNG，均保留原生窗口/PID/DPI/前景身份及SHA256侧车。中央分别为base、ours、ours；首行焦点统一到中央结果区，Control+Home定位首字符，未选择文字，三栏滚动为顶部。参考只读侧接受动作会把光标留在来源区，因此不能仅发Control+Home就宣称中央焦点；本批重新点击中央并确认。

首次reference-initial.png仍有系统鼠标光晕，reference-initial-idle.png把光晕移到空白区但仍在窗口内，均不计无遮挡证据。最终通过computer-use零位移滚动将指针停放在已观察的外部窗口空白处，再激活参考窗口；不更改系统设置、不擦除图片。完成态提示会遮住中央，Escape触发Cancel Merge确认，并非仅关闭提示；选择Continue Merge保留草稿后完成提示消失，再统一焦点采集。没有点击Apply或Discard。

实际侧由capture-conflict-reference-shell.cjs的--pair-focus统一结果区焦点与首字符，断言三栏scrollLeft/Top均0、草稿正确且磁盘未保存。最终actual-fonts目录包含三状态系统原图及pairFocus记录；CSS.getPlatformFontsForNode证明首行10字形使用自定义JetBrainsMono-Regular，没有回退。字体CSS分属性记录，避免font简写返回空串丢失证据。

## 量测结果与不通过边界

compare-clean-conflict.py比较原始相同尺寸图，不平移、不重采样、不过滤差异；支持独立实际目录。三状态完整不同像素分别为96154、101409、109168；正文区域y196至1127分别28774、29448、32315。分隔线[2,194,1728,196]每态3452像素全部一致。原先完成提示造成的遮挡已消除，不能将不同参考条件下计数减少归因于本批产品改动。

当前发现：左栏三行横向墨迹边界一致；中央字串末端实际比参考左2物理px，右栏起止均左4物理px。参考第二行ours墨迹y246–263，实际y245–262；首、第三行顶边一致。槽竖线参考x415–417、667–669、1316–1318，实际x416–418、665–667、1312–1314；颜色参考#EBECF0、实际#E9EAEE。参考正文底部y1127进入底栏，实际仍为编辑器；点线相位、光标闪烁和抗锯齿亦不同。此批确认了剩余差异，不以局部通过宣称整窗完成。

本地ThreeDiffSplitter.calcComponentsWidths按逻辑整数给左右各截断三分之一，余量归中央；当前CSS为分数网格等分。它是继续定位分栏差异的依据，但尚未证明足以解释上述全部偏移，未硬编码2px/4px。参考字体设置与默认源码证明配置为JetBrains Mono13、行距1.2，但当前窗口逐字形回退尚无直接内省。pair-initial/left/resolved.json因此把参考fontFallback留为null；pair-gate.json三个状态均拒绝，原因是缺少fontFallback。不能为了让门禁变绿填写未经证实的none。CLI合并入口也不代表完整Git冲突会话验收，55页完整合格数保持不变。

## 验证、哈希及清理

新增test_clean_conflict.py两项测试：三个状态分别注入单像素差异，验证完整/正文/横线区域计数；拒绝JPEG伪装及不匹配尺寸。2项通过，约2.3秒。真实统一焦点取证两次均完成并清理，最终约10秒；JS语法、git diff --check通过。不运行未改动产品的布局、全量模块或C#构建。

产品四文件MD5保持：mockup.js为1E09AB1418F80AD910ABFE4BBF524838，mockup.css为56BC1EB2D83B203A1890B50B0C37250D，live-data.js为00E7ECB134CBD0939313286BE7C92E38，bridge.js为F297BF8769E249CA0D804F4F4EEEF5A9。保留混合修改，未暂存、提交或回滚。

本批参考PID23656已taskkill /T /F；实际PID19688/65183/YH4soe、13588/65295/jKh0EX由finally停止子树、关闭浏览器并删除隔离profile。四份输入、PNG与JSON保留为正式证据，不是临时文件。没有启动常驻服务器，也没有重试历史被拒绝的清理。

```powershell
$pairPath='D:/github/Augit/artifacts/pixel-followup-2026-10-15/conflict-reference-clean'
$previousProperties=$env:PYCHARM_PROPERTIES
try {
  $env:PYCHARM_PROPERTIES='D:/JetBrains/PyCharm-2026.2.1-reference/reference.properties'
  $reference=Start-Process 'D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe' -ArgumentList @('merge',($pairPath+'/ours.txt'),($pairPath+'/theirs.txt'),($pairPath+'/base.txt'),($pairPath+'/result.txt')) -PassThru
  $reference.Id
} finally { $env:PYCHARM_PROPERTIES=$previousProperties }
# 使用本次启动所得PID；不得停止同名用户进程。
taskkill /PID <本次参考PID> /T /F
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-pair-manual-new $pw --light --screen-pair --reference-frame --pair-focus
# 实际驱动正常自动清理；异常时只停止其打印的本次PID。
taskkill /PID <本次实际PID> /T /F
python -B tools/audit/compare-clean-conflict.py $pairPath artifacts/conflict-pair-manual-new
python -B -m unittest discover -s tools/audit -p test_clean_conflict.py
```
