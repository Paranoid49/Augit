# 遮盖重定向反证与参考阴影复采（2026-10-15）

## 结论与范围

正常启动向测试基准对齐的产品修改和32状态验证见[startup-alignment-2026-10-15.md](startup-alignment-2026-10-15.md)。本批没有新增外观方案，不改变正常启动参数；Debug和Release随包web再次核对，各24文件与源码逐字节一致。完整像素整改目标仍未完成。

## 未保留的重定向试验

仅对捕获遮盖子窗增加WS_EX_NOREDIRECTIONBITMAP，CreateWindowEx扩展样式由0x08000020改为0x08200020，以检验子窗重定向表面是否造成合成变化。未改变业务DOM、模态结构或等待时长。

正常入口深色DDA无损录像323帧，51帧变化，最大采样间隔46ms，无重复原始时间戳。最严重第55帧（0.928秒）有199435像素变化，其中内部196364像素。六次刷新382.88/264.64/262.82/300.07/316.05/331.06ms；延迟达标不能抵消内容泄漏，严格整表面验收失败。DTS量化警告保留。

证据：artifacts/pixel-followup-2026-10-15/refresh-cover-no-redirection/dark。录像SHA256为5ae497ae46d6b1827d84404041c34fffcfe2cb9be3ccecb53db166ef3d8cb70e。试验前后Debug构建均成功，分别2.72/2.78秒。试验两行已撤销，ShellModalHost.cs恢复SHA256：5C7D5BDBC0BCFD2219BB9BBC3E2319E5EB3E9978653954EB2BC0402432DDA209；Release从未包含该试验。不把未采用方案计作产品整改完成。

## 新参考背景

仅在隔离PyCharm2026.2.1参考配置中关闭Show hard wrap and visual guides，应用后重开确认未勾选。Settings向下移动，取得1555×1215物理像素窗口与关闭后的同位置两份1811×1471原始系统PNG，四周保留128像素；DPI168、浅色活动态。没有修改日常PyCharm配置、系统DPI或PNG像素。

证据：artifacts/pixel-followup-2026-10-15/shadow-clean-reference-light。settings.png SHA256为14d4d7e6c69d91d54ed22336ec92228cb78938f9912feb16642c8c1da2154280；background.png与background-repeat.png同为cdb8eca13482a790bace786bc50adc8985fe4e05a499296ba33d57a1a5dd9e4b。背景原图有鼠标光晕，位于原对话框内部，未落入所比较的外沿；因此不将该图描述为整张纯色无光晕图。

复用已有实际shadow-full-frame-light原图与本次新参考，完整外沿774656像素按DWM左上角整数偏移比较。底部199040像素及左右下角各16384像素均为同背景、暗化量逐通道差0。顶部和左右两侧仍受旧实际背景工具栏分隔线影响，共五区背景不一致；不得筛除这些像素后宣称全外沿通过。old-actual-new-reference-ring.json保留完整结果和来源哈希，比较返回1表示未通过。圆角矩形内侧、深色及非活动态仍未完成。

## 资源、哈希与复验

重定向试验Augit10456、录制器21948已退出，端口63970无监听，Augit-shadow-pair-V1bml7已删除并另行确认。参考PID9120及其子树12172/10692/13860/20556/23340/408/11560已停止。没有重试历史被拒绝清理的目录；正式PNG、录像与JSON作为证据保留。

四网页MD5未变化：mockup.js=6E61081C842CA96770B3F8BB56E13B0A；mockup.css=99A3D1E61C7D12BA82DD25CE2E685A76；live-data.js=641B29F3BC50EC22500924F64B9C63CE；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。混合工作区未回滚、暂存或提交。Windows10已排除；跨屏DPI保留未验证。

日常启动与停止：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 正常关闭窗口；异常时仅停止本次启动器输出的PID。
taskkill /PID <启动器输出PID> /T /F
```

复查外沿采用tools/audit/compare-shadow-ring.py，四个位置参数依次为实际窗口、实际背景、参考窗口、参考背景，另传--radius 128和--output新的JSON路径。Python为D:/Python/Python3119/python.exe -B；不覆盖已有证据。参考启动仍使用隔离reference.properties与fixture，停止命令为taskkill /PID <本次参考PID> /T /F。
