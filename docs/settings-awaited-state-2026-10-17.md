# 设置状态等待与日常入口复验（2026-10-17）

## 根因及范围

用户报告设置重复、闪烁及正常入口与测试不一致。独立设置WebView及唯一子窗登记已在此前批次实现；本批先纠正验收自身的问题，随后根据原始录像反证调整初始搜索焦点顺序。

本机安装的Playwright等待实现直接对predicate()返回值判断真值。async函数返回Promise对象，因此最终为false时也可能提前完成。最小Edge反例实测legacyTimedOut=false、legacyResult=false；这解释了此前hostReadyMilliseconds小于宿主presentedMilliseconds的矛盾，不能把旧宿主就绪耗时及关闭耗时当作可靠性能验收。旧原生Stopwatch阶段值、原始录像和单独状态断言不因此自动失效，但不据此推断完整流程通过。

新增wait-async-condition.cjs，在Node侧显式await每次异步结果，只接受布尔true，有总超时并传播桥接异常。修复独立设置三处等待、设置阴影打开与关闭等待，以及冲突采集前客户区几何等待，共六处。设置新增计时先后断言和同一窗口句柄检查；影子测试改为检查特定失败原因，避免未实现的测试替身提前失败而假通过。响应探针失败和证据写入失败也不得跳过实例与临时目录清理。

本批另保留单次点击／八次并发请求分组和WM_NULL采样诊断；这些只用于审计，不引入产品常驻服务。PowerShell探针保留UTF-8 BOM以兼容项目编码检查，使用LF及中文注释。

## 实测

- 修正前单击十次原生呈现258–474ms；并发请求十次265–428ms，两组均未复现秒级慢开，不能据此宣称旧慢开不存在。
- 修正后的正常启动器完整八组通过：重复入口同一HWND、主窗无第二份表单、独立设置不创建模态快照、取消／OK／Esc／标题关闭、预览取消、保存失败重试、迟到保存应答后重开、Apply持久化；网页错误0。十次外部确认就绪426–593ms，宿主创建到呈现289–429ms；四条关闭59／124／109／67ms。
- 同批WM_NULL采样159次、超时0、最大9.8049ms。这仅证明发送消息采样时的响应，不证明GPU、网页或所有排队消息无延迟，也没有复现此前慢开时的线程状态。
- 正常启动器完整开关录像使用单次入口、真实默认profile与系统缩放、真实配置副本，保存不修改用户配置。前景身份门禁通过；该次外部确认就绪1409ms、原生创建至呈现985ms，OK关闭268ms。录屏自身有负载，不能拿无录屏最快样本替代该结果。画面存在系统指针光晕，必须计入取证限制。

证据：artifacts/pixel-followup-2026-10-17/settings-awaited-state、settings-awaited-cycle；诊断分组另见settings-repeated-open-stages、settings-message-response及前一天目录settings-single-click-stages。原始证据保留，不覆盖历史失败。

## 验证及基线

第一阶段修改审计脚本、测试和文档；后续最小产品修正见末节。异步等待五项及设置阴影四项测试通过；单击选项三项、响应分类两项通过。浏览器反例复验确认新等待器对假值超时、第三次真值才通过。日常入口两次均执行强制Debug构建，未运行无关全量测试。

分支dsh，HEAD为3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。保留既有混合修改，不暂存、不提交。第一阶段四网页MD5前后不变：mockup.js=2ACDC2382AEA5A93C0E948B78BA33731；mockup.css=D5342D22C245695A57F93BC90A1DC7ED；live-data.js=0F266297202DF61DBEB9D011211FC5FA；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

## 启停与清理

本批及接续诊断实例26616、10220、15956、11856、17264，探针19952、25788、前景采样12920均已退出；端口63018、59249、63418、63936、53780无监听。对应五个Augit-independent-settings临时目录已删除。浏览器反例由finally关闭独立Edge。证据PNG／JSON／MKV保留用于审计。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 停止首选正常关闭窗口；异常时仅终止上述启动器返回的本次PID。
taskkill /PID <本次PID> /T /F
# 定向复验；脚本finally回收本次实例与临时配置。
node tools/audit/independent-settings.cjs <Playwright模块路径> <新证据目录> --daily-launcher --expect-minimal-settings --message-probe
```

本批不完成整体像素目标：55页／全部模态同态配对、终端及冲突完整逐行比较、全部背景闪帧与延迟、系统阴影仍有缺口。Windows10按用户要求排除；跨屏DPI保留未验证。

## 原始录像反证

settings-awaited-cycle的353帧（2880×1716，最大间隔37ms）全部比较，第244帧4.385秒出现设置客户区整块白色，目视确认不是指针光晕。最大颜色包络外534341像素，最近整帧状态差995642像素。因此此次功能八组通过不能用来宣称无闪；完整呈现验收失败。视频SHA256为c417044b856b0ab3bb67cb9ffeba5c92b87c0ef7f78afb9c083267fe4efc8d36。记录器相对时间不直接等于桥接墙钟，尚不能仅靠事件时间判定该帧属于哪一个宿主调用。

## 初始搜索焦点接续

设置原先在window/settings-present返回、窗口已揭示后才设置搜索框DOM焦点。现移至宿主最终渲染确认前，使用preventScroll，保证初次焦点变化也纳入既有CapturePreviewAsync确认；不增加固定延时、截图遮盖或第二个业务窗口。architecture.md同步，同一live-data.js用于正常和测试入口。五项启动顺序、两项入口测试通过；新增真实焦点断言，单次正常入口专项通过。

settings-focus-before-present日常入口八组通过，关闭83／159／126／74ms，零网页错误；单次完整OK录像前景门禁通过，259帧固定标题区域未再检出修前全白候选，录屏原生呈现1190ms、OK关闭270ms。局部未复现不等于全部无闪，保留完整帧分析限制。Debug通过日常入口强制构建，Release强制非增量构建0警告0错误（6.21秒），两个目录各25项网页资源镜像通过。

最终live-data.js MD5为D64AB5C4E97AA7D3ED60F456555A34AE，其他三项不变。新增修改测试和产品JS均采用UTF-8/LF；文档声明修正新增探针后22个PowerShell脚本计数，检查通过。

修后完整259帧包络检查完成，排除0像素、RGB容差2、最大间隔37ms；最大越界24995像素位于第194帧3.635秒，原图目视为Saving settings及按钮禁用的合法保存状态，未再发现修前整块白窗。包络内部仍可能容纳错误混合，fullPresentationVerified保持false；单次未复现不能证明因果或所有场景无闪。录像SHA256为08ec7ebba960bf246f423151dda56de21f68018a19272366757ca2134f129c10。修后实例25924、6928及前景探针10320均已退出，端口55097／59669无监听、隔离目录r2V11E／ZoXzQA已删除；两段录像握手文件已删除，逐帧分析及解码进程已结束。
