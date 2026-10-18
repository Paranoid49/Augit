# 刷新等待期间切换文档保护（2026-10-17）

本批修复外部刷新与用户选择之间的竞态，不新增功能或页面。旧applyWorkspaceChanges在开始时记住当前路径，等待Git状态后无条件调用openDocument；如果期间用户切换、关闭标签或启动另一文件读取，旧批次仍推进全局documentToken、覆盖pendingDocument并触发前台加载提示，可能使用户新读取失效。

## 修改与回归

等待前记录读取代次，等待后核对live身份、当前路径、读取代次及pendingDocument。仍是原前台且无新读取时，保持原刷新路径；否则通过reloadChangedBackgroundDocument仅更新仍存在的原标签，不推进前台代次、不写前台加载提示、不抢选中或焦点。

后台结果接纳前再核对工作区对象、标签对象、原文档对象以及是否存在同文件新读取。关闭、替换、换工作区或同文件新读取使旧结果失效；读完切回原标签且无其他读取时才同步当前视图并要求批次渲染。失败保留旧内容并提示，关闭后失败不再干扰用户。已确认删除仍移除旧标签，不转成后台读取。

第一版三个回归在修前全部失败，分别为等待期间切换正文、新读取在途、关闭标签。初步跳过旧读取后继续核对规范，补齐后台标签更新，避免旧内容永久滞留。后续又用一个失败测试发现“批次开始前已有新读取”仍可能被抢占，最终补pendingDocument保护。首版与最终版实机记录均保留，不以首版代替最终验证。

## 正常入口证据

三批均由tools/start-augit.ps1强制非增量构建，真实配置副本、默认Session、正常DPI，附CDP。未注入响应延时或修改产品标志；操作真实临时文件，由产品监听触发刷新。

| 目录 | 实测结果 |
| --- | --- |
| refresh-switch-status | 首版四次实际点击均位于Git状态请求期间，前台other.txt保持，旧refresh.txt标签更新 |
| refresh-switch-status-final | 最终版四次实际点击均命中等待区间；距离请求开始30.6/34.3/37.3/39.2ms，距离应答还有16.3/13.6/13.3/12.5ms；后台得到本批内容，pending清空，切回显示generation4 |
| refresh-background-tab-final | 最终版四轮普通文件刷新与跳转行Esc关闭通过；最后撤盖577.1/458.2/477.5/461.2ms，首次仍超500ms |

以上目录均位于artifacts/pixel-followup-2026-10-17。实际点击时间由捕获阶段只读记录，并与桥接请求应答交叉核验；门禁必须真实命中等待区间，不能把普通切换通过算竞态通过。新文件读取在途、关闭及失败路径以定向自动化覆盖，未称所有竞态都实机覆盖。

普通刷新录像1244完整帧、最大间隔39ms，最差191变化像素，仍未无闪通过。录像SHA256=A07DFBD7FCF3151E69A140A73BE0F8BDC6ECDEA222B7880669686F46B4B2006A，无重采样或排除像素。本次不是闪烁、500ms或整体验收完成。

## 测试、身份与清理

最终32项文档/批次回归通过219ms，5项串行消费通过122ms；8项驱动/时序门禁通过1181ms，合计45项不同定向用例。9项共享资源、JS语法、UTF-8/LF及git diff --check通过；未跑全量。一次补丁传输转义失败没有写入，改用原始模板文本后成功。

最终实际执行live-data SHA256=55F32A4BB27BD073CE325B206543F8B6EC7E9EF5341FF8807E882B703E1D25CD；MD5从D50C01AF5311343AC7F8E03FA2E2F0FC变为E6D3B9263497E5B43E444348DAB3DC63。首版执行SHA256=685F2532CB11DE8AFEEF7786107D56DEAB0673331E8F38D4B399D91D5B7036BE，仅为历史阶段。其余网页MD5不变：mockup.js=0EEBB52037A8EEABDB81DA777E8C1FFE、mockup.css=00FA3357EC646E91030A9DFDD925DAA9、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

实际映射Shell仍B8F2BDAAD19F9B5740DF1BEC45CE1F84C38E26233CB77FB1558180BD54CE0B62、Infrastructure仍A08EB839CC37A2E71BFB5CF8C03C2BCD4FA74BE596CCECDC9D2222E12C27964B。UI Microsoft YaHei UI12、正文实际JetBrains Mono13、DPR1.75；Nahimic仍映射，只记诊断性能。Release未更新。

三份cleanup-independent.json独立核验19156、24232、4816及录像26604、监视2596退出，58273/64887/64143无监听，Augit-daily-refresh-TK56CU/B2qYYY/tTa5Js删除。单测夹具由finally清理，分析进程已结束；原始证据保留。历史参考清理与Nahimic权限阻塞保持，没有提交、暂存或回滚既有混合修改。

## 复验与后续

启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1

停止优先正常关窗；异常仅对本次核实PID执行taskkill /PID <本次PID> /T /F。

竞态复验：node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新目录> --git-fixture --switch-during-status

普通刷新复验将最后开关改为--gdi-clock；完整帧分析使用analyze-static-modal-video.py。

正常启动与设置慢开/关闭、背景透明帧/500ms、55页全模态、终端完整配对及完整系统阴影仍未整体通过。复杂Git/冲突精修继续暂停，删除尚未实施；Windows10排除本轮、跨屏DPI未验证，整体goal保持active。
