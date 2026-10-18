# 终端字体来源栅格对照（2026-10-16）

本批只推进字体来源诊断，不修改产品字体、字号或渲染参数。设置窗口沿用已完成的独立WebView与激活后渲染实现，详见settings-activation-render-2026-10-16.md；本批没有重复执行设置测试，也没有将局部字体诊断算作整体整改完成。

## 结论与边界

随包Regular WOFF2与独立安装Regular TTF，在同一个Edge155.0.4283.45进程的Canvas中，以13px、1／1.25／1.5／1.75／2倍变换、深浅主题绘制Ag 0123456789及95个可打印ASCII字符，共十组，RGBA逐像素差异全部为0，宽度及墨迹度量全部相同。每组确认有实际墨迹，排除空图假通过。

这排除了这些诊断样本中的资源来源差异；不是WOFF2解压后的sfnt逐表一致证明，也不是真实WebView2 DOM的LCD绘制证据，更不是PyCharm与Augit整窗配对。现存八行实机样本仍有每行1237个像素超过RGB2容差，不能宣布终端通过。不得以此把实际残差直接归因于某个渲染器。

两个现有Python环境均未提供fontTools；未安装依赖。改用已有浏览器解码并对照，产品依赖、字体分发文件均不变。参考JBR的TTF与独立安装TTF相同的旧证据见terminal-ink-diagnosis/ttf-table-comparison.json，本批没有复制参考字体到产品。

## 可复验产物

- tools/audit/font-source-raster.cjs：隔离浏览器诊断，要求新输出目录，finally关闭浏览器，不改用户profile。
- tools/audit/font-source-raster.test.cjs：两项测试通过，118.21ms，覆盖逐RGBA通道计数与空数据／残缺像素／尺寸不一致拒绝。
- artifacts/pixel-followup-2026-10-16/terminal-font-source-comparison/report.json及20张PNG：记录浏览器版本、源文件哈希、逐条件墨迹数量、度量、差异；actualWindowPair与sfntTableComparison均false。
- WOFF2为92164字节，SHA256 a9cb1cd82332b23a47e3a1239d25d13c86d16c4220695e34b243effa999f45f2；TTF为273900字节，SHA256 a0bf60ef0f83c5ed4d7a75d45838548b1f6873372dfac88f71804491898d138f。

JS语法、git diff --check、九项共享资产校验通过。未改C#或运行时文件，因此未重新构建或运行全量测试。当前四文件MD5前后不变：mockup.js=6A091BAA783027FBE2D040A48F7A737B，mockup.css=D5342D22C245695A57F93BC90A1DC7ED，live-data.js=0F266297202DF61DBEB9D011211FC5FA，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

## 后续定位

本地参考源码EditorUIUtil.setupAntialiasing明确设置LCD对比度和抗锯齿提示；StartupUiUtil.getLcdContrastValue从注册项或桌面提示读取，对比度超出100–250时回退140。源码只说明取值路径，不能冒充本次真实PyCharm绘制值。下一步需要实际终端绘制上下文及浏览器DOM字体来源／栅格证据；不再重复字号取整或通过像素偏移掩盖差异。

55页、全部模态、终端与冲突逐行及完整系统阴影仍未完成。Windows10按用户要求排除；跨屏DPI保留未验证。

## 启停和清理

本批仅启动诊断Edge，由finally关闭，结束后检查无headless／playwright临时profile命令行的Edge残留；未启动Augit、PyCharm或服务器。20张PNG与JSON是保留证据，不是临时文件。未触碰历史拒绝清理目标，未暂存、提交或回滚混合工作区。

```powershell
node --test tools/audit/font-source-raster.test.cjs
node tools/audit/font-source-raster.cjs <Playwright路径> <Edge路径> <新证据目录> web/vendor/jetbrains-mono/JetBrainsMono-Regular.woff2 <独立安装TTF路径>
# 诊断正常结束自动关闭；异常时只停止本次诊断父进程及其子进程。
taskkill /PID <本次诊断PID> /T /F
# 日常启动；停止请正常关闭窗口。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
```
