# 冲突运行时几何整改

日期2026-10-15，分支dsh、HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。承接已批准的PyCharm实机重排。Windows 10排除本次验证，跨屏DPI仍未验证；整体整改未完成，55页完整同态合格数未增加。未暂存、提交或回滚混合修改。

## 直接证据

新增只读premain代理tools/audit/ReferenceMergeGeometry.java，在AWT事件线程记录参考组件。临时VMoptions仅为本次进程添加javaagent与sun.font只读导出，没有改安装配置、正文或布局。代理定时器最多12次，每5秒记录一次；代理自检验证JSON转义，Java17编译通过。

证据位于artifacts/pixel-followup-2026-10-15/conflict-reference-clean/reference-runtime-geometry.json。实际262.9437.214运行时：ThreeDiffSplitter宽988，三栏313/314/313，两个24逻辑px连接带；来源槽宽79、中央46，分隔位置分别76/43，剩余3px为正文前留白。左正文Insets.left=18；中右正文left=0、right=18。三栏行高22、ascent16，Font2D指向参考JBR内JetBrainsMono-Regular.ttf、字号13；夹具字符均可显示且字形码非缺失。

该证据证明当前编辑器配置及物理字体，不等同于拦截所有实际绘制字形的回退路径。旧三状态pair门禁仍保留fontFallback=null，不以代理能力扩张为全量字体复原证明。reference-diagnostic.png尺寸1730×1215，槽线位置415/667/1316与旧无遮挡图一致。

## 修正及验证

共享mockup.js按ThreeDiffSplitter先扣连接带、左右向下取整、余量归中央。共享CSS正文前留白2改3px；槽分隔线由通用border改为INDENT_GUIDE，浅色#EBECF0、深色#313438。颜色来源为权威EditorGutterComponentImpl.paint与expUI light/dark配色；同步design-system.md和ux-spec.md。没有加入未解释的2px/4px位移。

- verify-conflict-result-gutter.cjs：24组通过，增加987/988/989逻辑宽度的整数分栏、3px留白、浅深配色断言。
- verify-ux-conflict.cjs：72组通过，覆盖主题、DPI、独立字号、按钮排布、首行空侧和编辑范围。
- 首次真实三状态采集成功，目录conflict-integer-split；该次包含整数分栏及3px留白，但早于最终槽线配色，不将其标成最终配色截图。
- 完整不同像素93952/99249/106733，正文26513/27229/29821，来源标题横线0。右栏横向墨迹误差由4px减为1px，中央仍2px，第二行纵向仍1px；抗锯齿等差异仍在。
- 最终配色两次补拍均被前景身份门禁拒绝，conflict-integer-final与conflict-integer-color-final没有合格系统PNG。未绕过门禁。最终配色通过计算样式和布局测试，系统截图仍待补拍。未跑全量产品测试或C#构建。
- 收尾共享9资产一致，受影响JavaScript语法、文档声明和git diff --check通过；本批源文件、规范与记录均UTF-8/LF。文档检查提示历史全量快照过期，未将其作为本批通过依据。

产品MD5：mockup.js从1E09AB1418F80AD910ABFE4BBF524838变为35E2450D6BEA6B5FE475D2BD3657ABC2；mockup.css从56BC1EB2D83B203A1890B50B0C37250D变为80F00477A5FEC1D0BEACD0048863A289。live-data.js为00E7ECB134CBD0939313286BE7C92E38，bridge.js为F297BF8769E249CA0D804F4F4EEEF5A9，均未改。

## 资源与手动命令

参考首个PID23476已停止；更新代理后PID21368仍在。自动审批以blocked by policy拒绝了停止PID21368与删除专用临时目录的组合命令，未执行、未绕过、未重试。临时目录为C:/Users/wl024/AppData/Local/Temp/augit-reference-geometry-86faa7b1bf5c4927aff332e739bfc1e7，保留probe.jar、class、manifest及VMoptions。不能宣称清理完成。

实际采集PID9656、11716、24464及各自临时仓库由驱动finally清理；51505/60408/62744端口复查无监听。专项浏览器均由finally退出；PNG、JSON及审计源码为正式成果。

```powershell
# 启动隔离参考（普通取证不加载诊断代理），用返回的PID对应停止。
$previousProperties=$env:PYCHARM_PROPERTIES
try {
  $env:PYCHARM_PROPERTIES='D:/JetBrains/PyCharm-2026.2.1-reference/reference.properties'
  $pairPath='D:/github/Augit/artifacts/pixel-followup-2026-10-15/conflict-reference-clean'
  $reference=Start-Process 'D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe' -ArgumentList @('merge',($pairPath+'/ours.txt'),($pairPath+'/theirs.txt'),($pairPath+'/base.txt'),($pairPath+'/result.txt')) -PassThru
  $reference.Id
} finally { $env:PYCHARM_PROPERTIES=$previousProperties }
taskkill /PID <本次参考PID> /T /F
# 手动清理，仅针对本批已确认的PID及目录；执行前确认PID仍属于本次参考实例。
Get-CimInstance Win32_Process -Filter 'ProcessId=21368' | Select-Object ProcessId,ExecutablePath,CreationDate
taskkill /PID 21368 /T /F
Remove-Item -LiteralPath 'C:/Users/wl024/AppData/Local/Temp/augit-reference-geometry-86faa7b1bf5c4927aff332e739bfc1e7' -Recurse -Force
# 启动/自动停止实际取证（新目录，不覆盖证据）
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-integer-manual-new $pw --light --screen-pair --reference-frame --pair-focus
# 异常时仅停止该次驱动打印的PID
taskkill /PID <本次实际PID> /T /F
python -B tools/audit/compare-clean-conflict.py artifacts/pixel-followup-2026-10-15/conflict-reference-clean artifacts/pixel-followup-2026-10-15/conflict-integer-split
```
