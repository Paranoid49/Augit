# 远端空态表格与修后原图（2026-10-17）

交接收尾：同步URL表头和数据列的6px内距，新增列起点断言修前失败、修后四组通过（4.02秒）。9项共享资产、测试语法及差异空白检查通过；两侧CSS MD5更新为F988C98DCB03385B301C87AD223FEF45。本次未重新构建或新增实机截图，不扩大原图结论，见[交接记录](session-handoff-2026-10-17.md)。

本批接续正常入口取证，补齐上轮Define正文溢出修复后的有效截图，并修复实机对照中可直接确定的表格差异。未改变远端字段、Git动作或嵌套模态机制。

## 证据与修改

PyCharm参考来自remote-normal-pair/reference-empty.png与reference-define.png（2026.2.1，DPI168）。实际走tools/start-augit.ps1、强制非增量Debug构建、默认WebView profile、当前配置副本及同一fixture；不强制DPI、字号、尺寸或测试场景。四张实际原图均经过目标HWND/PID、前后前景、边界、DPI及指针96物理像素清除检查，并目视确认表单无遮挡。PNG原始尺寸和SHA256已核对，不重采样、不裁齐两端以掩盖差异。

修前actual-empty为633×409，PyCharm为624×385；实际缺表格外框，No remotes偏左且表头使用白底。权威GitConfigureRemotesDialog.kt的createCenterPanel使用ToolbarDecorator包围JBTable，与实机的完整外框和居中Nothing to show一致。现共享CSS补外框、表头列分隔及透明底色，空态在表头下方剩余区域居中；共享JS改空态文案。标记不改变列表焦点、Add／Remove／Edit及唯一OK，非空行保持现有选择及滚动。

修后原始PNG确认上述结构生效。窗口尺寸仍为633×409，较参考宽9、高24物理像素；标题图标、字重、位置及工具条纵向仍不同，不登记整页像素通过。Define为709×463，参考710×363；实际多产品规定的Push URL行，不能把100px高差全部称为缺陷，也不删除产品字段迁就参考。修后正文溢出0、Name为origin、URL初始聚焦、Fetch默认勾选。

证据目录：artifacts/pixel-followup-2026-10-17/remote-daily-gated（修前）、remote-table-corrected（修后）。comparison.json明确pixelRestorationVerified=false，55页合格同态配对数量不因此增加。

## 失败记录

remote-daily-clean首次采集失败：电脑操作把指针移出时激活了禁用主窗，采集器发现目标远端HWND与前景主窗不同，正确拒绝。remote-daily-foreground在人工前景检查前等待超时，实例已退出，未采到图。后续保持指针在表单外，在正式模态创建后不再激活主窗，严格门禁未放宽。失败目录保留；不把电脑操作返回的关联窗口叠图当作屏幕证据。

## 验证与基线

- 空态浅／深×12／40字号四组，检查完整外框、正文居中、空表禁用动作；30行数据滚动不移动工具条和底栏。
- Define浅／深×12／13／20／40字号八组及限高检查通过，两项浏览器测试合计4.55秒。
- 远端受影响行为回归41项通过；真实嵌套Define取消归还Add、关闭Remotes保留Push、最终快照0及所有者启用、Git配置字节不变、网页错误0。
- Debug正常入口强制构建通过；Release强制非增量构建0警告0错误，6.45秒。两目录各25网页镜像一致，9共享资产一致；未跑无关全量测试。
- 分支dsh，HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；开始533项混合变化，未回滚、暂存或提交。
- mockup.js MD5：2ACDC2382AEA5A93C0E948B78BA33731 → CCE614AA84809724C103D6E98505DDE0。
- mockup.css MD5：D5342D22C245695A57F93BC90A1DC7ED → 16733F3E0D9ACAAC952EB5F5C3911FE7。
- live-data.js／bridge.js保持D64AB5C4E97AA7D3ED60F456555A34AE／F297BF8769E249CA0D804F4F4EEEF5A9。

## 启停与限制

日常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。

停止优先正常关闭窗口；异常时仅对启动器返回的本次实例执行taskkill /PID <本次PID> /T /F。取证脚本finally负责关闭自身浏览器、实例进程树及临时配置；原始PNG与JSON保留为证据。

55页及全模态、背景全部闪帧／延迟、终端及冲突完整逐行、系统阴影仍未全部完成。Windows10按用户要求排除，跨屏DPI保留未验证。

清理复核：四次实例9996／22452／1232／23116已退出，端口53893／55042／50664／54454无监听，对应临时目录uphLfK／n8zJp2／gfrIXr／tRFYMn不存在，全部本批.signal握手文件已删除。布局测试浏览器和41项行为回归静态服务均由finally关闭；UTF-8/LF、文档声明及git diff --check通过。
