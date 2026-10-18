# 同值侧栏刷新免捕获（2026-10-17）

本批消除正常入口可复现的无效背景捕获：提交侧栏可见时，外部新增空目录虽未改变可见Git列表，原地补丁仍写入同值文本和属性，引发一次遮盖、背景截图与撤盖。最终实机同场景DOM变更和三类捕获请求均为零；随后真实新增文件仍刷新列表及背景。不是全场景无闪、500毫秒或设置慢开验收。

## 范围与实现

按用户最新方向暂停复杂Git及三栏冲突精修，本批仅处理日常刷新。保留现有600项混合改动，不回滚、暂存或提交。依据ux-spec第2节：数据快照实际变化才更新区域；不改视觉稿或产品能力。

web/src/live-data.js中的patchChangesList、restoreChangesState和syncChangesToolbar仅在文本、属性、禁用状态实际不同才赋值。分组计数复用已有changesFileCount，避免初始渲染的1 file被增量补丁改成1 files。继续读取真实工作区、Git及目录状态，不屏蔽MutationObserver、不吞通知、不跳过错误恢复。

新增tools/audit/changes-noop-mutations.test.cjs执行真实产品函数和共享计数函数。修前同值调用产生20项DOM变更；修后同值及错误恢复后再次调用均零变更，真实状态色、勾选、工具栏禁用和错误提示继续更新，草稿、选区、节点与焦点保留。

## 正常入口证据

所有目录在artifacts/pixel-followup-2026-10-17下，均由tools/start-augit.ps1强制非增量Debug构建，真实配置副本、默认Session、正常DPI，附CDP。未添加产品测试条件。只写本批临时Git夹具。无桌面录像，不将DOM/桥接证据外推为像素验收。

| 目录 | 实测 |
| --- | --- |
| refresh-noop-before | 8项背景DOM变更，一轮modal-cover/refresh/uncover |
| refresh-noop-after | 第一版还剩两项分组文本变更，零捕获断言失败；保留失败证据 |
| refresh-noop-final | 新空目录仍读取git/status、workspace/changes与workspace/list；DOM变更0，捕获请求0，背景版本不变 |

最终visible-change.json记录随后新增new-visible.txt，列表三行真实显示，modal-cover/refresh/uncover完成且背景新版本已加载。原弹窗HWND保留，输入17、焦点不变。Esc后active=false、snapshotCount=0、captureCoverWindow=0、ownerEnabled=true，网页错误为空。第一版失败定位为单复数文案不一致，未放宽断言。

实际执行live-data.js SHA256=216793BB5DFDDE14470DDA0464C4F3CF1D9D170C78CEBF32118D0FBA25BD72DE；MD5由458DFB81C5F38777AFF4C91DA6380A81变为51F31F911876565442A0FA444FF0E84F。Shell仍94EF57F58A44D101CAECF8E3A8E9AD79B10E0C758E62BCCD42C8105ACF78EF46。其余三网页MD5未改，Release未更新。runtime.json、process-identity.json与mapped-images.json记录实际加载身份。DPR1.75，界面Microsoft YaHei UI12、正文随包JetBrains Mono13；未打开终端，不外推终端字体。

## 测试与清理

- 21项产品定向测试通过（约1.51秒）；计数修正后新增回归再次通过（约1.51秒）。7项refresh-daily-file审计回归通过（约1.14秒）。没有跑全量测试。
- 9项共享资源一致性、JS语法、22项既有PowerShell编码规则与git diff --check通过。
- 三批cleanup-independent.json独立确认PID25156/7892/19924退出，55706/63606/61114无监听，临时目录zubXP8/JkXEAl/5wB8cG已删除（均为Temp/Augit-daily-refresh前缀）。浏览器测试finally关闭，无本批服务器残留。
- 历史PyCharm清理自动审批阻塞和Nahimic服务权限阻塞保持，不重复被拒路径，不宣称全局零残留。

## 复验与后续

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1

停止优先正常关闭，异常仅针对本次核实PID：taskkill /PID <本次PID> /T /F。

正常入口复验：node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新目录> --git-fixture --scope-regression --trace-mutations --verify-noop

设置秒级慢开、无CDP完整开关、真实内容刷新闪帧与500毫秒仍未完成。下一步继续真实内容刷新呈现阶段定位，不重做已否证候选。55页/全模态及阴影也未完成；复杂Git与冲突精修暂停，不能记为通过。Windows10排除本轮，跨屏DPI未验证，整体goal保持active。
