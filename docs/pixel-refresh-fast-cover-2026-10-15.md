# 遮盖截图提速与恢复合成隔离（2026-10-15）

上一轮修改跳转行真实组件布局，属于产品进展。本轮推进背景刷新目标，保留遮盖截图提速；透明帧、完整500ms分布和总体目标仍未验收完成。

## 保留的产品修改

ShellModalHost的遮盖首图和缓存遮盖重捕获改用既有ShellSurfaceCapture快速PNG路径，原样传递字节。初始所有者背景仍使用CapturePreview。原生子窗关系、业务DOM、两帧确认、DwmFlush、图片队列和资源生命周期均不改变；无新依赖、无产品远程调试端口。该修改不通过新增固定等待换稳定。

深色正常入口六次刷新414.23/295.17/285.05/285.23/280.51/282.94ms；遮盖复用捕获89.98–99.29ms，历史同主题旧接口152.91–177.41ms。跨运行存在负载差异，不能将单次首次差值冒充稳定性能分布，但六次及捕获阶段均提供提速证据。浅色六次396.21/262.32/260.18/301.75/328.34/296.91ms；因先执行inspect-cover，其录像开始已复用遮盖，396.21不能冒充首次创建成本。

证据目录为artifacts/pixel-followup-2026-10-15/refresh-fast-cover/{dark,light,lifecycle}。普通入口Light/Dark、UI12、JetBrains Mono13、系统DPR1.75；隔离配置/profile和固定夹具，未改用户设置。

## 完整表面仍失败

两组DDA原始无损录像均以1820×1341完整客户区、RGB容差2、排除0像素验证。深色327帧/56变化帧，最坏12796像素（内部0）；浅色335帧/51变化帧，最坏9717像素（内部0）。最大采样间隔38/42ms，原时间戳无重复；解码时DTS量化警告保留。两个sampledWholeSurfacePassed/fullPresentationVerified均false。不因本次未采到内部露底撤销既有深色失败证据。

深色录像SHA256：618888c2f7e2f81314098fd59d949907af79e9c4831074f05486cb3481a46291。
浅色录像SHA256：2c391b6c029e271f0f0671b4dae32b673294bfa95d33c5bec712861d60c5bbff。

浅色加载源PNG2452×1630、按原物理偏移[316,145]裁取，与正常表单/遮盖可见/真实表单隐藏三份系统原图正文内部均逐像素相同。全客户区仍各12796差异像素，集中边缘；内部16px分区仅供定位，没有从验收中剔除边缘。PNG快速编码增加字节量，遮盖样本235350字节，不能称内存优化。源图比较不是两个捕获接口的严格同内容A/B，也不代表过渡帧通过。

## 未采用的恢复捕获试验

新增仅审计的--capture-before-uncover：截获撤盖请求，先读取恢复后的CDP合成图并保存，再原样交还请求；失败记录且仍交还，清理不重复发送。三个测试覆盖等待、失败和在途清理。该钩子没有装入产品。

refresh-restored-capture/dark六次恢复捕获全部成功，额外112–150ms；整体627.55/521.33/460.04/448.66/465.56/506.46ms。DDA396帧/90变化帧，最坏12778边缘像素，内部0，最大间隔44ms。恢复原图已目视有完整Settings，最坏系统帧同样仍有完整正文但边缘变化。不能因此证明已定位过去的内部露底，也不能用额外捕获作为产品修复；继续加帧等待没有足够依据。录像SHA256：21281c61900d608635996fa426afb864f0a5060d2b8aa695f570d838f6b7bd51。

## 验证与交付

捕获/准备/图片队列19项通过；新增连续捕获失败后恢复用例后，捕获类8项通过。仅跑受影响范围，未重复全量。真实单组验证并发关闭、重开、嵌套父层更新、图片尺寸、草稿/焦点及关闭释放，geometry.json保留详细结果。3项审计钩子测试、脚本语法、C#定向格式检查通过。

Debug构建1.56秒、Release8.31秒，均0警告0错误；两套web各24文件与源码一致。ShellModalHost.cs SHA256：DC74DB1791922E24A441EADC216C28B3979C9E0D791F16024F935118DD7FF5BC → 5C7D5BDBC0BCFD2219BB9BBC3E2319E5EB3E9978653954EB2BC0402432DDA209。
四网页MD5未改：mockup.js=6E61081C842CA96770B3F8BB56E13B0A；mockup.css=99A3D1E61C7D12BA82DD25CE2E685A76；live-data.js=641B29F3BC50EC22500924F64B9C63CE；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。未回滚、暂存或提交混合工作区。

## 清理与复验

本批Augit6960/13012/4300/9852、录制器4940/6256/22492及其子树均退出；端口58705/62145/64772/58101无监听。临时sRES5n/e2qjZk/xknExY/ozW5wc目录已独立确认删除。历史自动审批拒绝的目录未重试清理，按原记录保留。

日常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。
正常关闭；异常仅用taskkill /PID <本次启动输出PID> /T /F。
定向录像：node tools/audit/capture-shadow-shell.cjs <Playwright模块路径> <新目录> Dark --normal-entry --refresh-video --trace-surfaces --desktop-duplication --repeat-static-frames。
完整表面分析：D:/Python/Python3119/python.exe -B tools/audit/verify-refresh-surface.py <新目录>。

下一步仍需解决遮盖/原生框架切换的边缘合成差异和内部露底，不能将截图提速当作稳定性修复。55页及全部模态、终端/冲突完整逐行配对、完整系统阴影均未完成。Windows10排除，跨屏DPI保留未验证。
