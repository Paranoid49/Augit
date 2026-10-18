# 背景快照物理像素整改（2026-10-14）

## 根因与修改

原始系统阴影图发现背景文字变化。新增采样诊断确认：175%缩放、1400×930逻辑主窗，CapturePreview得到2452×1630的PNG，原实现把图片宽度固定为2450÷1.75逻辑像素，即压缩到2450物理像素，高度按比例压缩。背景文字被全图重采样，不能归因于字体或ClearType差异。修前真实尺寸见`snapshot-source-dark/snapshot-source.json`。

`ShellSnapshotPresentation.ImageSizingScript`改为使用PNG的naturalWidth/naturalHeight除以捕获时的固定比例；`ShellModalHost`在首次加载和每次替换图片时调用，仍由控制器边界裁切，不拉伸图片。捕获比例不随跨屏显示比例改写，跨屏验收仍未完成。没有改产品行为、字体、共享CSS、宿主协议或图片队列。

## 验证与证据

以下路径均位于`artifacts/pixel-followup-2026-10-14/`。

| 项目 | 修前 | 修后 |
| --- | --- | --- |
| 深色同位置文字带，2424像素 | 156像素通道差大于2，最大129 | 0像素差，最大0 |
| 浅色同位置文字带，2424像素 | 156像素通道差大于2，最大174 | 0像素差，最大0 |
| 深色右/下近边相对PyCharm最大暗化差 | 阴影上批已修复 | 0/0，通过 |
| 浅色右/下近边相对PyCharm最大暗化差 | 阴影上批已修复 | 1/0，通过 |
| Augit四边128像素采样带尾部 | 左侧文字污染 | 深浅两主题四边均收敛 |

文字带是同一系统截图坐标`[0,463,4,1069)`；范围有限，不代表整个背景或页面逐像素一致。原始系统PNG及稳定重复背景见`snapshot-sizing-dark-fixed`、`snapshot-sizing-light-fixed`；定量汇总见`snapshot-sizing-comparison.json`。`snapshot-sizing-detail.png`是4倍最近邻诊断放大图，仅辅助目视，不作为原始量测输入。`snapshot-source-0.png`是CDP控制器诊断图，明确不是系统截图或CapturePreview原始响应。

初次诊断尝试fetch原图被资源类型限制拒绝；canvas导出被同源限制拒绝，两次均未取得图，不算通过。随后采用CDP控制器截图及naturalWidth/布局元数据定位尺寸，像素结论仍来自独立系统PNG。未修改浏览器安全设置。

- 两项实际注入脚本测试通过：五个比例的真实尺寸/更新定尺、两帧呈现确认。
- 八项ShellSnapshotImages/ShellModalOperations测试通过，限定两产品文件的格式检查通过。
- Debug构建9.18秒，0警告、0错误。
- 两主题真实Settings打开/关闭、背景两次哈希一致、快照释放和错误收集通过。
- `snapshot-sizing-refresh-checked`真实单组通过：主背景更新的版本8/5、嵌套父层更新的版本11/1/0均保持PNG物理尺寸，关闭后快照与遮盖归零；合并期间/捕获后变化边界仍通过。
- 首次刷新365.94ms（20ms轮询、单样本），不是稳定性能或系统逐帧无透明证据。上一组未加尺寸断言的同流程393.78ms也保留，不作性能A/B结论。

四主要网页MD5前后未变：mockup.js `6298632B16BA82E5FCFB9B0AB177593D`；mockup.css `06751CCD396E8ECACD19779CBBDE38AB`；live-data.js `DB52470B2BAB434DE4E463086A39FA6D`；bridge.js `F297BF8769E249CA0D804F4F4EEEF5A9`。工作区分支dsh、HEAD `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`，混合修改未回滚、暂存或提交。

## 复验与清理

```powershell
$pixelPlaywright='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node --test tools/audit/snapshot-presentation.test.cjs
dotnet test tests/Augit.Shell.Tests --no-restore --filter 'FullyQualifiedName~ShellSnapshotImagesTests|FullyQualifiedName~ShellModalOperationsTests'
# 使用新的证据目录；脚本自动启动并在finally停止实例、删除隔离profile。
node tools/audit/capture-shadow-shell.cjs $pixelPlaywright artifacts/snapshot-dark-new Dark --inspect-snapshot
node tools/audit/capture-shadow-shell.cjs $pixelPlaywright artifacts/snapshot-light-new Light --inspect-snapshot
node tools/audit/run-pixel-remediation-shell.cjs $pixelPlaywright src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe artifacts/snapshot-refresh-new --single --reference-fixture --cold-refresh --coalesce-refresh
# 异常中断仅停止驱动本次打印的PID及子树。
taskkill /PID <本次AUDIT_PID> /T /F
```

本批失败诊断4264/49934、13648/61305，成功诊断3624/49378，修后4356/63417、7760/63254，更新回归15352/51722、17316/59755均由finally停止并删除各自隔离profile。上批两个被自动审批拒绝删除的目录仍保留，不以其他工具绕过，详见阴影记录。证据文件按审计要求保留。

整体整改仍未完成：55页完整同态配对及全模态状态、终端/冲突完整逐行双端配对、系统前景透明帧、阴影四角/非活动状态仍需证据。Windows10按用户指示排除本轮验收，跨屏DPI保留未验证；不改变产品支持范围。
