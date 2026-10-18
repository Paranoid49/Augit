# 设置主题预览与回读顺序（2026-10-17）

## 结论与范围

主窗接到 settings/committed 或 settings/closed 后会异步回读持久化配置。若等待期间收到较新的 settings/preview，旧回读收尾曾把新预览改回持久化主题。bindIndependentSettingsEvents 现独立记录预览代次：回读仍更新已保存配置，但只在没有较新预览时应用主题；取消后的新回读仍恢复已保存主题。没有新增功能、页面或等待。

用户认为复杂 Git 界面投入过多，并指出三栏冲突解决界面没有必要。继续暂停复杂 Git 和冲突精修，优先正常启动、设置与刷新；本批未删除现有能力，也不把暂停项目登记为通过。整体目标未完成。

## 回归与正常入口

新增 tools/audit/settings-preview-reload.test.cjs，执行真实绑定函数并控制应答顺序，覆盖提交晚到、关闭晚到、取消恢复、无效预览及新回读淘汰旧结果。接续记录中修前两项失败，修后四项通过；本次与 settings-typography-reload、settings-open-singleflight 合计13项通过，181.73毫秒。

正常入口脚本 tools/audit/independent-settings.cjs 增加 --preview-reload-cycle。首批 settings-preview-generation 失败：浅色按产品约定不设置 body.dataset.theme，审计却直接读取它来选择相反主题，导致再次选择 Light，Apply 保持禁用。已只修正审计对缺省浅色的判断，原失败证据保留，不修改产品以迁就测试。

最终证据目录 artifacts/pixel-followup-2026-10-17/settings-preview-generation-final：正常 tools/start-augit.ps1 强制非增量 Debug 构建、源网页、真实配置副本、默认 Session、正常 DPI，附 CDP。Light→Dark→Apply→继续预览 Light→Cancel 恢复 Dark→重开→预览 Light→OK，配置与主窗最终均恢复 Light。两个预览检查点的主窗、设置 DOM 与原生 surfaceColor 一致，未捕获网页错误为空。

两次原生 presented 为288/278毫秒。此次没有重排实机应答，没有桌面录像，仅单次打开而非并发压力；不能把正常操作回归当成实机竞态复现，也不能消除无CDP秒级慢开、白帧或全场景关闭缺口。

## 实际身份与验证

- 分支 dsh，HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；保留既有混合修改，未暂存或提交。
- live-data.js MD5：C281B1EAC844573EE688419F893F6A3A → E1EE6539727817718DBBFC752747BB46。主窗及设置实际执行 SHA256 均为62E1C15EC0056B5C261EEF0EEF7B0E91E7023E3C3C446F7B13DA1F2F71B12A3F，与磁盘源一致。
- Shell SHA256仍为94EF57F58A44D101CAECF8E3A8E9AD79B10E0C758E62BCCD42C8105ACF78EF46；process-identity.json、mapped-images.json核对实际进程与映射，Release未更新。
- mockup.js / mockup.css / bridge.js MD5仍为0EEBB52037A8EEABDB81DA777E8C1FFE / 00FA3357EC646E91030A9DFDD925DAA9 / F297BF8769E249CA0D804F4F4EEEF5A9。
- 两窗DPR1.75、界面Microsoft YaHei UI12；主窗实际正文为随包JetBrains Mono13。未打开页面不外推。
- 两个修改JS语法检查、九项共享资源镜像校验及 git diff --check 通过；未运行全量测试。

## 清理与复验

两批 cleanup-independent.json 独立确认PID9120/3240退出，端口64369/52572无监听，临时目录 Augit-independent-settings-FRP6uN/A2VMtK均删除。没有启动额外服务器。历史PyCharm实例清理自动审批阻塞及Nahimic服务权限阻塞保持，不宣称全局零残留。

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1

停止优先正常关闭；异常只对本次核实实例执行：taskkill /PID <本次PID> /T /F。

定向回归：node --test tools/audit/settings-preview-reload.test.cjs tools/audit/settings-typography-reload.test.cjs tools/audit/settings-open-singleflight.test.cjs

正常入口复验：node tools/audit/independent-settings.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新目录> --daily-launcher --preview-reload-cycle --single-open

慢开、刷新透明帧与500毫秒、55页/全模态、终端完整配对及系统阴影仍未完成。Windows10排除本轮，跨屏DPI未验证。后续不得为已暂停的复杂Git精修重开采集。
