# 设置排队重开与正常入口复核（2026-10-16）

## 结论

修复新表单在旧关闭或背景捕获等待期间提前绘制的竞态。重复入口、快速重开、嵌套返回的定向测试及Debug/Release真实宿主回归通过；默认配置、默认profile的日常启动实测只观察到一个设置窗口。背景刷新仍有原生边缘闪动，用户报告尚未全部解决，不能将本批功能通过等同于全程无闪。Windows10排除，跨屏DPI仍未验证。

## 根因与修改

此前bindNativeModalHost在running分支直接排队返回，新表单的nativeCapture隐藏标记要等队列释放之后才设置。旧window/modal-close应答未到，或modal-cover/modal-refresh仍等待时，新节点已可见，可能进入旧宿主或主窗口的合成表面。现将未呈现表单的隐藏提前到排队判定之前；已经呈现过的父表单返回保持可见，不重新捕获，不重建草稿。没有新增WebView、桥接方法或外壳架构。

新增产品函数测试：关闭应答被扣住后再次打开，修前nativeCapture为undefined，修后为true；另覆盖等待cover和refresh期间新嵌套表单的隐藏及恢复。四份相关测试共20项通过。实机驱动增加--delay-close，只扣住审计实例的真实关闭应答，检验新表单computed visibility为hidden、无新增present请求，释放后原生depth=1。该场景检查确实发生关闭的宿主与网页回包竞态，不替换产品关闭实现。

## 真实宿主和日常入口

证据根artifacts/pixel-followup-2026-10-16，子目录settings-queued-reopen-debug及settings-queued-reopen-release。两套均使用真实仓库、默认配置副本、9个标签、随包网页、隔离profile，不加scene或强制DPI参数。延迟呈现时重复触发入口，五次均只发一次present、depth=1；分类切换、快速重开、嵌套返回及五条关闭通过，网页错误0。

- Debug关闭save/cancel/escape/header/apply-save：204.39/171.99/145.38/171.86/209.34毫秒，嵌套返回120.90毫秒；两次真实无变化通知没有触发背景捕获。
- Release：212.69/215.74/199.69/232.26/226.35毫秒，嵌套返回142.61毫秒。
- Debug由tools/start-augit.ps1 -PrepareOnly非增量构建，Release显式非增量构建，均0警告0错误。两套24个web镜像逐文件一致，共享9资产一致，相关JS语法和git diff --check通过。未改C#产品逻辑，不重复全量Shell测试。

另直接执行tools/start-augit.ps1，使用用户真实配置、默认profile和真实仓库，PID26996；真实鼠标点击设置后，桌面与可访问性树均只观察到一个Settings原生窗口和一个网页对话框。没有场景、调试端口、临时profile或会话重置参数。工具对禁用所有者下Cancel点击报告目标为msedgewebview2而非Augit，重新观察激活后重试仍失败，因此手动关闭不记通过，最后只结束本轮26996及其子树。默认配置没有保存外观修改。静态观察不能证明打开动画全程无重复帧。

## 桌面闪帧仍失败

settings-queued-reopen-dda为正式Debug、正常场景、参考fixture、隔离profile、Light、DPR1.75。DDA/FFV1共352帧，每帧1820×1341，首帧固定、RGB容差2、排除0像素。2帧变化，最坏第73帧4064像素，其中角部131、直边3933、内部0；已查看原始最坏帧。最大源帧间隔37毫秒、重复时间戳0；保留FFmpeg解码DTS量化警告。前景101次采样、最大间隔99毫秒，无观测干扰，不能排除采样间干扰。严格门禁退出1，sampledWholeSurfacePassed及fullPresentationVerified均false。

六次刷新509.96/351.82/339.84/325.35/310.77/304.26毫秒；首次仍超过500毫秒。录像SHA256为edba91a2e936e530caa9b577098edd4fca21d42f59785f900364ea286e0be888。没有本批完整打开/关闭动画录像，不宣称当前无内部变化否定历史露底。剩余应继续定位业务表面、遮盖与DWM边缘提交的关系，不重复已经失败的延长等待或移除边框试验。

## 变更与资源

live-data.js MD5由A104830E74369E116C81ED898B4A9FFC变为38A6F63C55532F4D67A210E5ED933F88。mockup.js=396E779B261AC31FBE4B625181134680、mockup.css=15DE14CA11BB3E8682E403FB7EF5E054、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9保持。既有混合修改未回滚、暂存或提交。接续前ReferenceMergeGeometry的componentGraphics诊断修改保持原状，本批未编译或验证，不计本次交付。

本批Augit12688/25724/11916/26996、前景13536、录像25744及子树已结束，端口54422/55387/50457释放；三个隔离目录jfiw3s/epii1v/fj7CCv由驱动finally删除并独立确认。此前参考PyCharm28592通过Escape、Alt+F4及退出确认正常结束。删除其临时目录C:/Users/wl024/AppData/Local/Temp/Augit-goto-origin-paired-8b4966aeec594e5b94cc3b8eb78fd2b6的命令被自动审批以blocked by policy拒绝，没有重试，该目录仍存在；此前拒绝目录也未触碰。

## 启停及复验

```powershell
# 日常启动，自动强制非增量构建；停止请正常关闭窗口。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 异常时只使用本次启动打印的PID，不能按进程名结束其他实例。
taskkill /PID <本次PID> /T /F
# 定向回归，驱动finally停止自己的实例并清理profile。
node tools/audit/check-settings-close.cjs <Playwright模块路径> <新证据目录> --many-tabs --delay-cover --delay-present --delay-close --nested --bundled-web --navigation
```
