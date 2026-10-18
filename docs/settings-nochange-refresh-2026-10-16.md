# 设置重复窗口与无变化背景重绘接续（2026-10-16）

## 本批结论

重复设置入口修复经当前Debug和Release再次验证：呈现回包前多次触发仍保持同一DOM、单次呈现、原生depth=1。新增修复文件监听的无效全区域重绘；真实背景变化的边缘闪烁仍未修复，不宣称本次用户报告全部解决。

## 根因与修改

强制非增量构建后，真实仓库回归在分类切换阶段失败。原始navigation.json记录src/Augit.Shell/obj/Debug/net10.0-windows/win-x64目录通知，随后applyWorkspaceChanges无条件重建statusbar、bottomTool和titlebar，触发modal-cover及modal-refresh。分类操作本身不能解释这次重绘，直接来源是文件通知。证据：artifacts/pixel-followup-2026-10-16/settings-current-user-regression。

web/src/live-data.js现在在读取前后比较既有buildSnapshot和错误状态，目录树另行比较。无变化仍读取磁盘、Git及已展开目录，但不重建可见区域；真实状态变化、错误恢复、目录增删、当前文档或Diff内容变化、冲突及Git元数据路径保留更新。没有硬编码忽略bin/obj，也没有暂停设置期间的文件监听。依据ux-spec第6.2节；参考VcsLogRefresherImpl.kt第130–140行的数据包变化判定，不把两者实现称为完全相同。

新增workspace-change-render.test.cjs，执行产品函数，修前7项中4项失败，修后7项通过。既有订阅消费、设置单飞、模态归属合计15项通过；真实通知消费模块另5项通过。check-settings-close新增两次真实忽略文件写入验证：在设置打开期间通知被消费，未产生重复呈现、遮盖或背景捕获。

另纠正compare-startup-paths证据清单中遗留的-SkipBuild文字：实际日常启动早已强制构建，但清单仍错误宣称跳过。新增针对清单的测试，不改产品启动语义。

## 实机结果

Debug和Release均强制非增量构建，0警告0错误；真实仓库、9标签、随包网页、默认配置副本及隔离profile，延迟呈现、重复入口、延迟遮盖、分类和嵌套通过。Debug额外覆盖脱离节点及真实外部两次写入。网页错误均0。

- Debug五条关闭save/cancel/escape/header/apply-save：181.87/189.36/161.57/180.50/191.26ms，证据settings-nochange-final。
- Release：216.27/163.07/154.95/186.34/191.94ms，证据settings-nochange-release。
- 两套各24项web镜像与源文件一致，共享9资产一致；JS语法及git diff --check通过。未改变C#逻辑，不重复全量Shell测试。

日常tools/start-augit.ps1实际非增量构建并启动验证通过，证据settings-nochange-daily；使用真实配置副本，未加场景、DPI或会话重置参数。记录的live-data.js SHA256为9610efe8b7b9896351c97d29ea47b4c474b5c95d77e880a04d5ba5e2ec4b463e。PID2984已退出、61425端口释放、临时目录Augit-startup-compare-UuMG9X已删除。日常入口3项命令与证据测试通过；该入口检查未扩展成55页验收。

## 仍失败的整表面录像

settings-nochange-dda为普通场景、Light、DPR1.75、UI12、JetBrains Mono13。DDA/FFV1采样349帧，每帧1820×1341，首帧固定、RGB容差2、排除0像素。80–83帧各9696像素变化：角236、直边9460、内部0；最坏原帧已目视。最大源时间间隔35ms、重复时间戳0；解码DTS换算警告保留。前景104次采样无观测干扰，最大间隔98ms，不能排除采样间干扰。严格门禁失败。

六次实际刷新602.32/350.02/332.29/349.01/313.72/307.14ms。首次仍超过500ms。录像SHA256：9a24070a3f0a9b9af708edb7ca18783ba57ad158200bc3d1584056859bb7c3a5。不得把本次未采到内部露底解释为历史内部露底已修复；sampledWholeSurfacePassed和fullPresentationVerified仍false。

## 哈希与边界

live-data.js MD5由AD3BA8F7851F463ABF99FF932B4CA95A变为A104830E74369E116C81ED898B4A9FFC；mockup.js=396E779B261AC31FBE4B625181134680、mockup.css=15DE14CA11BB3E8682E403FB7EF5E054、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9不变。未回滚、暂存或提交既有混合修改。Windows10排除，跨屏DPI未验证。

## 启停与资源

本批Augit18420/9356/4572/27856、录像23612、前景18212已退出；58767/65303/54099/57768无监听。四个隔离目录已独立确认删除。上一轮PyCharm25548经原生退出确认正常关闭；临时目录Augit-goto-origin-dy86bm4b的组合清理命令遭自动审批拒绝，未重试删除，不能宣称该目录已清理。

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。停止请正常关闭窗口；异常只针对本次输出PID执行taskkill /PID <本次PID> /T /F。
