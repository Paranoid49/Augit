# 冲突结果行号槽与字体整改（2026-10-14）

## 实测与修改

本批继续[三栏逐行对照](pixel-conflict-observation-2026-10-14.md)。使用computer-use技能打开既有独立PyCharm2026.2.1的merge四文件入口，没有应用结果。取得前景原始PNG：`artifacts/pixel-followup-2026-10-14/merge-gutter-metrics/reference.png`，1730×1215、DPI168、比例1.75，SHA256为`08a506d098e7a947ac33bb4861849d08cd03a5353ee4ed009b7f7764d5208a84`。已目视确认三栏正文、状态和窗口没有被其他窗口遮挡。

参考中央编号槽约在x592至668之间，宽76物理px，约43.43逻辑px；两侧另含接受与忽略动作空间。旧Augit三栏统一使用至少三位编号加54px，中央也预留了动作空间。现中央改为实际最大编号宽度、最低16px，再加27px留白；正文比槽末端多2px，编号右侧21px。默认中央宽43逻辑px，即75.25物理px，与本样本差不到1物理px。27/21/2为此参考场景测量派生，不宣称是全部字号、所有DPI下已验证的上游常量。

权威源码`EditorGutterComponentImpl.java:892–894,2091–2100,3145–3160`确认最大编号按字体度量，New UI另有断点图标宽度下限；`EditorGutterLayout.java:194–276`确认编号、图标、绘制区独立排布。当前本地checkout与参考二进制版本并非完全相同，尺寸仍保留原图互证和范围限制。

新增多位行号测试首先失败：字号13时，编号120实际宽21.72，而中央按正文度量为24。诊断发现正文启用`font-variant-ligatures:none`后，`getComputedStyle(body).font`为空；原`gutter.style.font = ...`未生效，三栏行号退回Microsoft YaHei UI12。改为逐项复制字体族、字号、字重、字形及连字配置，保留正文专用字距。此修复作用于三栏，不仅中央；两侧槽的ch也随正确等宽字体恢复。

## 验证与边界

- `verify-conflict-result-gutter.cjs`24组通过：浅/深、DPR1/1.75、正文13/40、4/120/10001行，检查实际行号字宽、留白、字体、正文内距和数字不进入可编辑正文。首次失败已用于定位字体问题，不改期望掩盖错误。
- 真实深色多块`merge-result-gutter-final`通过：三栏公共行对齐、逐块键盘动作、撤销、纵横滚动复用、精确保存。`multi.json`确认行号JetBrains Mono13、字距0.200012px，横滚固定、纵向对齐差0。
- 真实浅色短流程`merge-result-gutter-light`通过：追加、撤销、焦点和槽背景；中央槽43px，两侧77.39px。原始客户区PNG先经三色标记验证，最终浅色图已目视。初次深色短流程`merge-result-gutter`在字体修复前，仅保留为中间证据。
- 9共享资产、修改JS语法、文档声明及`git diff --check`通过。未运行仓库全量或重新构建未修改的C#模块。

本批只解决中央多余动作留白及三栏编号字体错误。整体外壳、工具栏、列标题、底部动作、连接带、两侧槽宽、边框仍与参考不同；本次参考与Augit窗口尺寸不同，不能计入55页完整同态配对。中央留白的跨字号精确像素还需参考矩阵，24组仅证明自适应与无重叠。Windows10不验收，跨屏DPI保留未验证。

## 命令与清理

```powershell
$pixelPlaywright='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$pixelChromium='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/audit/verify-conflict-result-gutter.cjs $pixelPlaywright $pixelChromium
node tools/audit/capture-conflict-reference-shell.cjs artifacts/merge-gutter-multi-new $pixelPlaywright --multi
node tools/audit/capture-conflict-reference-shell.cjs artifacts/merge-gutter-light-new $pixelPlaywright --capture-only --light
powershell -NoProfile -File tools/audit/verify-ui-assets.ps1
node --check web/src/mockup.js
node --check tools/audit/verify-conflict-result-gutter.cjs
git diff --check
# 参考启动，保存并恢复进程环境；输入来自已存在的隔离证据夹具。
$pixelPrior=$env:PYCHARM_PROPERTIES
try {
  $env:PYCHARM_PROPERTIES='D:/JetBrains/PyCharm-2026.2.1-reference/reference.properties'
  $pixelInputs='D:/github/Augit/artifacts/pixel-followup-2026-10-14/merge-reference'
  $pixelReference=Start-Process 'D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe' -ArgumentList @('merge',"$pixelInputs/ours.txt","$pixelInputs/theirs.txt","$pixelInputs/base.txt","$pixelInputs/result.txt") -PassThru
} finally { $env:PYCHARM_PROPERTIES=$pixelPrior }
# 取证结束仅停止本次参考；Augit驱动正常由finally停止，异常中断时按打印PID停止。
taskkill /PID $pixelReference.Id /T /F
taskkill /PID <本次Augit_PID> /T /F
```

参考PID3768已停止，独立查询参考路径无进程；base/result的SHA256均为`14F6DB9E5F72A87AFEB79DFF24E93B5C9E4C457CFA060134CB7C58B977B51CF5`，未应用参考结果。Augit PID23008/22536/23196、端口54890/65421/60086均无残留；临时仓库/profile `Augit-merge-pair-0Y419r`、`z5hddY`、`bAqf1Z`已删除。测试浏览器在finally关闭，无常驻服务器。证据原图与JSON保留，未重试历史审批拒绝的清理。

修改共享mockup.js/CSS双份及新验证脚本。mockup.js MD5：`8BF2C396751DF1FA9A164E6EBC772886`→`3BE58955301492EC6DA96F177A45BA54`；CSS：`CB5E64C420FEA13B86EEEE3098BB455C`→`C124EBD0CDC60E7EC24F7485F948A399`；live-data/bridge维持`DB52470B2BAB434DE4E463086A39FA6D`/`F297BF8769E249CA0D804F4F4EEEF5A9`。混合工作区保留，未暂存或提交。
