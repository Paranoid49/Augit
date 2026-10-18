# 重开回归修复后的屏幕复验与裁切诊断

日期2026-10-16。上一批双设置表面重入修复保持；本批未修改产品代码，不把关闭专项通过当成刷新无闪验收。55页/全部模态、背景刷新、终端/冲突逐行及阴影四项目标保持未完成。Windows10排除本次实机验收，跨屏DPI保留未验证。

## 当前正常版本的系统录像

证据目录：artifacts/pixel-followup-2026-10-16/refresh-after-reopen/light。Debug当前产物、正常入口、Light、UI12、175%系统缩放；配置与profile隔离，工作区为已有PyCharm参考fixture，不是用户实际工作区。通过computer-use技能选择实际返回窗口并激活后释放主窗检查点；未使用scene或pixel-exact。录屏覆盖已打开Settings的六次背景刷新，不包含首次打开、关闭、重开的整个动画，不能当成这些过渡的逐帧证明。

Desktop Duplication无损FFV1共342帧，每帧1820×1341，固定首帧、RGB容差2、排除0像素。62帧变化；其中2帧直边变化，全部62帧含角部变化，内部变化0。最坏75/76帧各4060像素（角部131、直边3929），最大通道差115；第75帧原图已目视检查。最大采样间隙34ms，无重复源时间戳。ffmpeg解码到rawvideo时报告时间基换算后的DTS重复警告；源ffprobe时间戳和解码帧数核验仍通过，不把警告掩盖成完整Present证据。

前景身份103次采样、最大间隙103ms、qualified=true；仍无法排除采样间干扰。录像SHA256：36b8e7e46627d64d97b9e02ea22d0cb9381d25f33e1cecbdbf4d150166db1bef。严格整表面门禁退出1，fullPresentationVerified=false。六次延迟601.59/372.51/391.94/343.38/354.80/358.93ms，首次仍超过500ms。首次遮盖捕获224.88ms，控制器创建114.18ms、导航115.20ms（并行阶段不能相加作总耗时），图片就绪76.11ms。

## 裁切捕获没有进入产品

为减少遮盖整页PNG成本，新增probe-cover-clip.cjs，只在隔离实例比较Page.captureScreenshot整页、表单逻辑裁切及设备像素取整裁切。每组交替顺序四次，原始PNG保存，不改变生产ShellSurfaceCapture或模态队列。两份证据为cover-clip-probe/light和cover-clip-probe/aligned。

首次整页/裁切中位数106.02/80.25ms；第二次整页/逻辑裁切/对齐裁切为109.11/81.57/80.69ms。PNG由242707字节降至104388字节。但每份裁切图相对取整整页裁图均有3019个不同像素，其中1096超过RGB2，最大差10，变化范围[2,78,1818,554]。

严格复核发现表单原始物理上边为144.5000095，下边1485.5001011，实际落在半像素；先前直接取整到145/1486的比较不足以证明裁切API自身损坏画质。comparison.json已明确pixelAligned=false，只保留为整数裁图诊断，exactPixelMatch=false。新增测试确保即使这种比较得到零差异，也不能算精确验收。未重采样原图，未改变产品几何去迁就比较，未采用裁切优化。下一步若继续此方向，应先取得与宿主实际裁切一致的坐标映射，再验证系统合成画面；不能直接使用省下约25ms的结果。

## 接续：实际宿主坐标及布局试验

新增--native-screen校验，按capture.json中真实liveBounds裁切[316,145,2136,1486]，不再以DOM半像素取整推断宿主。对照同一实例原始系统截图，整页PNG内部差异0、边缘差异12796；逻辑裁切和像素对齐裁切均内部差异2987，其中1096超过RGB2、最大差10，四组一致。因此拒绝裁切优化；不能为此改变产品几何。原图身份、哈希、DPI、边界及内部/边缘诊断补充测试，Python合计5项通过。

refresh-layout-center/light为另一隔离试验：保持实测尺寸，以inset/margin代替transform居中，前后几何完全一致。368帧中65帧角部变化、1帧直边变化、内部0；最坏第83帧4064像素（角131、直边3933），最大差115。最大间隙38ms，无重复源时间戳。前景107次采样、最大间隙93ms、未记录干扰；不证明采样间没有干扰。首次667.58ms，后五次347.88–371.50ms，严格门禁失败，fullPresentationVerified=false。录像SHA256为1fd106deb0aec8577fa0777f57c866a6eb2de4fb62e983a346415cf593e96532。不同录像不用于断言性能因果变化。

布局候选未采用，审计脚本中的临时--layout-center-probe入口已移除，原始证据保留。Augit25940、前景25704、录像28196均退出，59230无监听，Augit-shadow-pair-Z7Nsw1目录不存在。后续首图交接产品修复另见settings-close-runtime-2026-10-16.md，不把本节失败录像改写成通过。

## 变更、验证及清理

只新增probe-cover-clip.cjs、probe-cover-clip.test.cjs、compare-cover-clip.py、test_cover_clip.py，并在capture-shadow-shell.cjs增加显式--probe-cover-clip诊断入口；默认运行行为不变。两项JS参数测试及三项Python像素/越界/半像素拒绝测试通过，相关JS语法和git diff --check通过。未改C#，未重复构建或全量测试。

产品四文件MD5保持：mockup.js=44D6C53684B74DBC6116FF25F7C22932，mockup.css=D5DADE8F4DB84506031A9AB3B1F5DB80，live-data.js=3DCFCEAED3358BE87D4FA3A69080E7FA，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。未暂存、提交或覆盖混合改动。

Augit6276/28356/25868、前景采样25668、录像26952均已退出；端口61565/60529/60599无监听。临时Augit-shadow-pair-lx2cx7/P6Xpfg/qh8M9H均已删除，Python测试TemporaryDirectory自行清理。原始证据保留，未触碰历史拒绝清理目录。

```powershell
# 日常启动；停止为正常关闭窗口。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 录像或裁切诊断，必须观察并激活本批主窗后才释放输出的检查点。
node tools/audit/capture-shadow-shell.cjs <Playwright路径> <新目录> Light --normal-entry --refresh-video --trace-surfaces --desktop-duplication --repeat-static-frames --activate-main
node tools/audit/capture-shadow-shell.cjs <Playwright路径> <新目录> Light --normal-entry --probe-cover-clip --activate-main
python -B tools/audit/compare-cover-clip.py <裁切证据目录>
python -B tools/audit/verify-refresh-surface.py <录像证据目录> --require-stable
# 脚本finally回收；异常时仅停止本次日志输出的实例。
taskkill /PID <本次PID> /T /F
```
