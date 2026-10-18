# 背景表面捕获提速与合成追踪（2026-10-15）

2026-10-15遮盖提速：首图及复用改用快速PNG，深色六次281–414ms，浅色复用260–396ms；整表面边缘仍变化，透明帧未验收。Debug/Release已更新，当前哈希、限制、验证及清理见[本批记录](pixel-refresh-fast-cover-2026-10-15.md)。

## 接续：当前实现的同机时钟样本

artifacts/pixel-followup-2026-10-15/refresh-current-clock/dark 保留本次普通入口深色 GDI 录像及墙钟关联。SHA256 为 2bc2cd6b1494ab1582becb7cd605fcb64835fc388c4f9ed73179f5462d970356，431 帧、36 帧变化，容差2、排除0像素；最大仅10个圆角像素变化，最大通道差5，内部及非角部边缘变化均为0。第40帧变化位于 cover 请求后329.098ms、响应前24.902ms；其它角部变化亦可跨越 refresh 阶段。此样本没有复现上一批 DDA 的内部露底，不能用于定位内部露底发生在哪个阶段，更不能推翻上一批失败证据。没有据此追加等待或改动显示逻辑。

本组首次观察527.79ms，后五次328.89–363.29ms；整表面及500ms全部样本验收仍不通过。最大采样间隔50ms，无重复时间戳；GDI采样和墙钟关联不等于完整桌面呈现证据。分析器退出码0仅表示分析执行成功。

采集与分析完成后独立检查 PID22076、ffmpeg PID2664 已退出，端口53648无监听，临时目录 Augit-shadow-pair-ZeQjEc 已删除。本次未修改产品代码，当前实现哈希与下文保留版本一致。

## 本批结论

保留背景刷新截图接口的局部提速，不宣称透明帧修复或500ms整体通过。RefreshSnapshotAsync改用WebView2的CallDevToolsProtocolMethodAsync调用Page.captureScreenshot，PNG、fromSurface=true、captureBeyondViewport=false、optimizeForSpeed=true。首开背景与遮盖首图仍用CapturePreviewAsync；表单DOM、窗口顺序、裁切、等待与生命周期未改变。没有引入依赖，也没有开启产品远程调试端口。

ShellSurfaceCapture验证响应必须包含非空图片、合法Base64及PNG尺寸头，原样返回字节；失败交给原流程恢复表单与遮盖，保留旧背景，不默默替换为空图。头部验证不是完整PNG解码，图片加载仍由现有WebView完成。

## 同内容核对与代价

在同一隐藏DOM任务序列中依次调用新接口和原CapturePreview，得到两张2452×1630原始PNG：RGB逐像素完全一致，最大通道差0。证据为artifacts/pixel-followup-2026-10-15/refresh-surface-capture/content-check/下surface.png、preview.png、comparison.json。该内容核对包含额外捕获，不能并入延迟样本。临时硬编码路径和写图代码已删除。

快速PNG为193048字节，原PNG为110815字节，样本增加82233字节，约74.2%；协议还临时持有Base64字符串。换取的是编码/捕获阶段耗时下降，不声称内存更省。现有图片版本合并与释放机制保持，持续内存成本仍需后续测量。若内容、比例或生命周期回归，不能仅以速度保留该接口。

## 正常入口桌面录像

均为普通入口、系统DPR1.75、界面12、正文JetBrains Mono13、1820×1341完整表面、RGB容差2、排除0像素。目录前缀为artifacts/pixel-followup-2026-10-15/refresh-surface-capture/。

| 样本 | 首次流程 | 后续5次流程 | modal-refresh往返 | 录像结果 |
| --- | ---: | ---: | ---: | --- |
| light | 458.69ms | 341.56–396.19ms | 95.3–117.7ms | 347帧/53变化帧，最坏9717像素边缘变化，内部0 |
| dark | 517.49ms | 317.58–384.58ms | 89.3–132.7ms | 357帧/54变化帧，最坏199435像素，内部196364 |

深色原始第64帧已解码并目视：Settings消失、显示底层业务正文，不是仅阴影或光标。保留interior-change-frame64.png。浅色本组未见内部变化，不能消除深色和历史失败。sampledWholeSurfacePassed及fullPresentationVerified均false。

Light录像SHA256为71507cced2a6dbcca2876eda81a2e795746c2b862655d0fa7c7e91166be96357，Dark为d5d96d2741e14346782ee3543af1fa6696934f3d46b02354cc9be443b932d767。DDA解码输出时间基转换DTS警告；ffprobe原时间戳无重复且解码帧数匹配，最大采样间隔63/46ms，不代表完整Present。单次首次样本不构成稳定性能分布；本批不重复全量性能测试。

## 合成追踪的确定事实

新增trace-refresh-compositor.cjs，通过CDP记录cc/viz/gpu/user_timing事件，显式页面标记角色。只用于隔离单层Settings，按业务视口尺寸区分背景和遮盖，不能泛化为嵌套页面识别。异常路径结束追踪、关闭IO流、分离会话，有2项定向测试。

旧接口基线refresh-compositor/light中，业务PID12172、背景20736、遮盖7464，三者独立且均有合成提交。65091条事件、12457013字节原追踪，排除本样本中“三个页面共用渲染进程”的猜测；不证明GPU或桌面合成没有问题。原录像375帧/74变化帧，最坏2296447像素，仍失败。DDA相对时间没有强行配准为追踪UTC，不能凭这些事件宣称找到了具体白屏根因。追踪增加负载，571.70ms首次不与无追踪样本当作严格A/B性能比较。

## 验证、产物与清理

新增7个捕获测试用例，连同图片、队列、代际和尺寸定向共25项通过；模块完成后Shell完整201项通过（13秒），未重复Core/Infrastructure无关全量。新C#文件格式检查通过。真实单组外壳验证连续背景更新、PNG尺寸、搜索草稿/焦点、嵌套父层更新不污染主背景、回到同一父窗及关闭释放；证据lifecycle-repeat/geometry.json。首次lifecycle因标题栏异步重绘移除审计标记而超时，移动标记到不被区域重绘替换的app根背景后重跑通过；没有通过修改产品隐藏该测试失败。

Debug构建及Release构建均0警告0错误；Release末次3.15秒。两套随包web各24文件与源码逐字节一致。ShellModalHost.cs由3CEA5A1ED2EE7BD55EF6F9AE93CDDE607567167080E795BB459732266756B070变为DC74DB1791922E24A441EADC216C28B3979C9E0D791F16024F935118DD7FF5BC；ShellSurfaceCapture.cs为33CFB7A8673F4CCA2A3409CF0E6B26CDD455B34FD2DB8603AEFF684FE7C9A4F7。

四网页MD5前后相同：mockup.js=612E9976704343ABE1BB8778B077508F，mockup.css=089ED8741BD87C8A4F95501E3485526E，live-data.js=8A39A72F1D0F8FC7D53D3F158205B2C7，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。未回滚用户混合改动、暂存或提交。

本批Augit PID22160/2152/19800/25092/19948/26344，录制器11560/22952/6636/24920均退出；端口57094/49674/53937/59070/51441/52639无监听；临时目录Ie93Sc/aEwOSl/biC16r/UVvUqD/v4dYrW/adApgu已独立确认删除。证据为保留交付物。

复验：node tools/audit/capture-shadow-shell.cjs <Playwright模块路径> <新证据目录> Light --normal-entry --refresh-video --trace-surfaces --desktop-duplication --repeat-static-frames；驱动finally清理。日常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。正常关闭窗口，异常时只对本轮输出PID执行taskkill /PID <PID> /T /F。

下一步继续隔离CapturePreview之外仍复现的遮盖/桌面合成失效；55页与全部模态双端同态、终端/冲突逐行、完整系统阴影均不因本批完成。Windows10排除本次验证，跨屏DPI未验证。
