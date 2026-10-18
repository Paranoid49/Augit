# 设置激活后渲染确认整改（2026-10-16）

接续日常启动器真实录像检出的白屏，不用静态截图和功能测试代替呈现证据。此批沿用户已批准的独立设置WebView架构整改，不新增截图遮盖、业务DOM副本、固定等待或渲染参数覆盖。

## 失败与定位

settings-actual-launcher-cycle的420帧中，第167／168帧为整块白色客户区。为区分首次呈现和分类切换，record-settings-cycle.cjs新增 --record-phases：打开完成后、Terminal分类及返回Appearance后分别静置1200ms。等待仅存在于诊断驱动，不修改产品。

修前分段录像settings-actual-launcher-phases共482帧。固定标题区域[600,220,200,50]扫描在第143–145帧、2.682／2.703／2.710秒发现10000像素全白；该区域不随分类改变。首次原生呈现1385ms，关闭121ms，所有前景采样属于本实例。该局部扫描仅定位白屏候选，不冒充整画面验收；原始录像保留。扫描输出rawvideo时间基警告，但解码帧数与原始ffprobe逐帧数一致；后续候选扫描显式指定微秒时间基。

## 实现

旧顺序在CapturePreviewAsync完成后，才Show、SetForegroundWindow及MoveFocus，然后解除DWM cloak。这意味着已确认的合成表面之后仍受到窗口显示和焦点切换影响。现在窗口消息先在cloak期间完成这些操作，再等待设置自身CapturePreviewAsync，将结果写入Stream.Null丢弃；完成后投递SettingsRevealMessage，只负责解除遮蔽及应答。主窗口不搬移、不截图。销毁和关闭后续接继续检查归属，不能重新显示旧窗口。

修改ShellWindow.Settings.cs、ShellWindow.cs及隐藏启动顺序测试，architecture.md同步。产品网页内容和四文件MD5不变。Debug及Release均使用同一实现。

## 验证

- 五项隐藏启动测试通过，新增断言保证CapturePreview在MoveFocus之后且揭示消息在渲染完成之后。
- 分段录像选项及保存校验六项通过。日常启动器测试四项通过。
- 因本次调整外壳呈现生命周期，运行一次Shell模块完整测试：226项通过，0失败、0跳过，15秒。未反复执行全仓测试。
- Debug日常启动器强制重建、Release强制重建均0警告0错误；C#定向格式、JS语法、git diff --check通过。
- Release独立设置八组通过，网页错误0，四条关闭101／196／64／113ms；创建期及显示后重复请求保持同一窗口，保存失败重试、主题取消、迟到保存、Apply重开通过。25项随包资源与源码一致。
- 同批Release十次原生呈现603、502、499、527、549、561、519、2769、545、561ms。存在2.769秒慢开；全帧分析与该回归并行产生本机负载，不能凭此归因或证明性能改善。 最慢样本控制器创建从5到2308ms、导航2339ms、表单准备2572ms、渲染2768ms，主要等待发生在控制器创建阶段；settings/read仅2.8ms。该分段说明本样本等待位置，不代表已定位WebView内部原因。
- 修后settings-activate-before-render分段录像504帧，固定标题白屏候选0；首次呈现1579ms、真实脏草稿OK关闭42ms，版本只增一次。慢开没有解决，局部候选0不能宣布全画面无闪。完整504帧2880×1716分析完成，排除0像素、RGB容差2、最大采样间隔56ms；最大整帧最近状态差86406像素位于第257帧（4.636秒），原图已目视确认为Terminal分类。未再检出修前整窗白屏；第446–503帧共58帧与最终关闭画面一致。仍有合法状态、焦点及背景文字差异，fullPresentationVerified保持false，不扩大成全部场景零闪结论。录像SHA256为21dc36c9aebd9aa5e851d16b09ec56b2aed6a470d6d091095a1ce80649697cc0。

## 启停

本批修前分段实例22700及录屏22580／前景监测5512，修后实例12768及录屏28052／前景监测17960，Release实例26228均已停止；63673／57478／63355无监听（63355仅有TIME_WAIT，PID0）。临时目录jokeIs／DjsPPQ／hXc5VL已删除。默认profile保留；分析Python18908与解码ffmpeg27612也已退出。未触碰历史被拒绝清理目标。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 优先关闭窗口；仅异常时停止启动器打印的本次实例。
taskkill /PID <本次PID> /T /F
```

整体55页、所有模态、终端和冲突逐行、完整阴影仍未全部验收；Windows10按用户裁决排除，跨屏DPI未验证。混合工作区保持，未暂存、提交、回滚。
