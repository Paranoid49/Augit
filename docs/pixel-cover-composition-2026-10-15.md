# 遮盖源图与系统合成定位（2026-10-15）

## 结论

整体目标仍未完成。本批取得源PNG与三份同位置系统截图，确认源PNG为不透明RGB，深浅样本中实时表单隐藏前后的完整系统截图哈希相同。深色一组原表单到遮盖有10个角点变化，最大5级RGB；另一组首次遮盖前就已经是较亮角点，三图完全相同。不能把后一组零差异解释为修复，也不能直接把差异归因于透明通道。初始桌面合成状态仍需固定。

本批三个产品试验均已撤销，保留上一批导航并行和完整呈现保护。实际进展为缩小根因范围、加入可重复取证工具和负向检查；不登记背景刷新通过，不改变原目标。Windows10排除本次验证，跨屏DPI保留未验证。

## 实施与实测

capture-shadow-shell.cjs新增--inspect-cover：等待后台更新空闲，记录前后宿主状态，按实际窗口尺寸找到遮盖控制器，启用CDP Page域后用Page.getResourceContent读取已经加载的内存PNG；取得遮盖显示、底层表单隐藏后的系统截图，最后恢复并撤盖。源图与系统图明确区分，不修改跨域安全设置，不用画布重采样替代源文件。

verify-cover-composition.py检查截图来源、无重采样、活动状态/窗口/DPI稳定、进程和窗口身份、同一边界、哈希、PNG真实尺寸及alpha范围。原表单到遮盖沿用整表面2级容差；底层隐藏前后采用零容差。记录priorCoverBytes，旧证据缺字段为null，不默认是首次；fullPresentationVerified始终false。

证据根为artifacts/pixel-followup-2026-10-15：

| 目录 | 结果 |
| --- | --- |
| refresh-cover-source-dark-settled | 源图2452×1630 RGB；原表单到遮盖10像素变化、最大5；隐藏前后0差异，完整PNG哈希相同 |
| refresh-cover-source-light-settled | 源图同尺寸RGB；原表单到遮盖最大1；隐藏前后完整PNG哈希相同；未记录priorCoverBytes，不证明首次 |
| refresh-cover-final-dark | priorCoverBytes=0；三张系统PNG完全相同，但基线角点已等于上一组遮盖后的颜色；不能外推为解决 |

深色源PNG SHA256为7ca63bc8b3abed867c15a14cff3b4c3d73e37fa99b26f7b1b9615a6110715ef9，浅色为d6b19d1721e2fe33662449596d15108e1fbcfd0af239edb015623b938db1608c。深色坐标(7,4)/(4,7)/(2,13)最初系统色为(38,39,41)/(40,41,44)/(31,32,35)，遮盖后为(40,42,45)/(42,44,47)/(34,36,39)，源PNG对应像素为(33,34,37)/(36,37,41)/(28,29,32)。源图不透明并不保证不同窗口合成后的屏幕色完全一致。

## 未采纳试验

- 子窗显示隐藏加SWP_NOREDRAW：深色445帧/45变化帧，只捕获角点最大5RGB；浅色430帧/42变化帧，仍有3760像素边框闪亮、最大134RGB，首次552.69ms。浅色含1个重复时间戳，未删除。四边中央半段64px阴影暗化剖面相对上一批最大差均0，但不能用阴影不变替代整表面失败。试验已撤销。
- image-rendering:pixelated：435帧/46变化帧，严格模式失败，未证明有收益，已撤销。
- 将透明控制器底色改为实时主题底色：静态深色三图一度完全相同，但随后首次录像427帧/16变化帧，最严重2421905像素变化、最大217RGB，首次571.46ms。不能采用，已恢复Transparent。

各目录refresh-surface-no-redraw-dark/light、refresh-surface-pixelated-dark、refresh-surface-opaque-dark均保留原始录像和失败报告；没有放宽2级容差或排除角部。源图取证早期因复用控制器、画布跨域、CDP未启用和控制器截图超时失败；已分别改为尺寸定位、直接资源读取、启用Page域和仅系统截图。所有失败实例finally清理，未记成通过。

## 验证与资源清理

6项新增分析测试通过（0.193秒），覆盖边缘单像素、窗口身份、透明源图、尺寸/哈希篡改、来源/越界和复用基线。最终实际--inspect-cover流程通过并生成分析。最终恢复产品后的Debug构建0警告0错误（2.36秒），JS语法与git diff --check通过。仅跑受影响验证，不跑全量套件。

产品网页四MD5与上批一致：mockup.js 3B3399F68FD796B48952022625C3B5F4；mockup.css 09D3687D25A62F2377D117F3AE78599F；live-data.js B48E37B755997A2C28D98AF8CB87E7ED；bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。分支dsh、HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；未回滚、暂存或提交已有改动。

本批Augit PID9580/6560/14388/21472/4720/20476/21352/19644/18820/6284/2016/17092，录屏PID5348/1536/11704/19876均已退出。端口49686/60317/56884/64616/50499/63978/65337/60293/63506/59842/52226/56985无监听。隔离目录w99vRn/9XOdEt/bDD6Zn/DTZUac/jgHLah/dyxtgM/uB6tiv/bq9ovW/5JawCQ/h6Eb74/np3Adz/HR4UMj均已删除；测试临时目录由TemporaryDirectory清理。正式证据保留，未启动PyCharm，未重试历史被拒绝的清理。

运行命令（驱动finally自动清理；仅异常中断时使用本次输出PID）：

~~~powershell
$coverPlaywright='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/capture-shadow-shell.cjs $coverPlaywright artifacts/cover-source-new Dark --inspect-cover
python -B -X utf8 tools/audit/verify-cover-composition.py artifacts/cover-source-new
python -B tools/audit/test_cover_composition.py
taskkill /PID <本次Augit_PID> /T /F
taskkill /PID <本次recorderPid> /T /F
~~~

后续应同时记录最早稳定系统基线与首次遮盖身份，查明为何未创建遮盖时角点已出现两类状态；不能再用单份静态零差异选择实现。55页/全模态、终端/冲突逐行及完整系统阴影配对仍待完成。
