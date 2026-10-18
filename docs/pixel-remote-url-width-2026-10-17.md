# 远端长URL列宽（2026-10-17）

用户已批准远端按实机调整并保留Push URL。本批仅修正既有列宽计算，未增加功能或页面，未改数据行高或字段。整体像素配对未完成。

## 权威与修改

本地`D:/github/intellij-community/plugins/git4idea/backend/src/remote/GitConfigureRemotesDialog.kt`的updateTableWidth使用Table.font派生Font.BOLD，同时计算名称和URL宽度。当前mockup.js却在URL测量前切回普通字重，长URL预留不足。两侧共享mockup.js现统一以已有600字重度量名称与URL，正文renderer仍为400。design-system.md同步明确测量与显示的区别，未修改视觉稿迁就运行时，两侧保持字节一致。

新增真实共享页面回归使用长URL，修前实际636、期望667逻辑像素，断言失败；修后667通过，URL显示400。空态、列分配、浅深及12/40字号、受限宽度和30行滚动原回归一起3项通过4.64秒。远端完整行为41项通过，9共享资产、Debug25资源镜像、JS语法、git diff --check通过。未运行全量测试。

## 正常入口

新增remote-url-width.cjs建立临时本地Git仓库，仅配置example.invalid远端、不取回或推送。经tools/start-augit.ps1强制非增量构建、真实配置副本、默认Session打开真实Push再进入远端管理；取消后验证Git配置逐字节不变、无前端错误。没有伪造DOM远端或用审计场景。

第一次remote-url-bold-daily因审计误读remotes数组路径而等待超时，PID20280、端口57396，finally完成清理。修正为remotes.remotes并添加15秒局部超时后，remote-url-bold-daily-final成功：PID28384、端口50796、实际一行、期望及实测667逻辑像素、DPR1.75、URL字重400。没有取得本数据态的系统PNG或参考对照，screenCaptured=false、pixelRestorationVerified=false，不增加55页通过数。

实际映射Shell SHA256=`94EF57F58A44D101CAECF8E3A8E9AD79B10E0C758E62BCCD42C8105ACF78EF46`，Infrastructure=`1F4F34550F6D601C9850A4F068BD03F9AC85A44347BF058DF2FBB37FFBC65E80`，仍有Nahimic模块。实际执行mockup.js SHA256=`8BCE57BFBE9A105837ACE35E8B9E03C60C044BDB0E2700AE59BA6A690AFAD886`；标题字符Microsoft YaHei UI已取样，UI12/正文配置13、DPR1.75。正文等宽节点未打开，不外推字体。

四网页MD5：mockup.js从F24517B92AC7A46D6343F3E214C333B2变为`0EEBB52037A8EEABDB81DA777E8C1FFE`；mockup.css=`00FA3357EC646E91030A9DFDD925DAA9`；live-data.js=`DE85823861C020608F28DE997197B555`；bridge.js=`F297BF8769E249CA0D804F4F4EEEF5A9`。Debug已更新，Release未更新。

## 清理与后续

最终cleanup-independent.json确认两PID、两端口、Augit-remote-url-GcNAoi及Augit-remote-url-tJ4NSZ目录均不存在。行为测试自行finally关闭浏览器和服务器。历史参考PID21240与指定目录虽获用户清理授权，自动审批仍拒绝；不得用本批清理覆盖该阻塞。Nahimic停止亦已获授权，但服务权限拒绝，前后Running/Auto/PID5848；两项完整记录见refresh-background-loaded-2026-10-17.md。

启停及复验：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 优先正常关闭；异常只停止已核实本次PID。
taskkill /PID <本次PID> /T /F
node tools/audit/remote-url-width.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新证据目录>
```

下一步仍需数据态双端同态PNG（包含长URL、选择与滚动），核对参考20px行高与当前24px规范边界后再实施；不把空态、度量正确或本地回归当作整页通过。设置慢开/闪帧、刷新500ms、终端和冲突完整逐行、完整阴影以及55页全模态仍未结案。
