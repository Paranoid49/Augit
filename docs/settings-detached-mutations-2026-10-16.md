# 设置脱离节点通知误刷新（2026-10-16）

## 结论与范围

修复已移除设置节点的观察器通知被归为主窗口背景变化，进而多余执行遮盖、隐藏表单及截图的问题。重复入口和关闭代次修复保持；原生边缘闪动与此前整面露底没有在本批获得解决证据，不宣称用户报告全部修好。Windows10按用户要求排除，跨屏DPI未验证。

`web/src/native-modal-state.js`在归属判断前排除`mutation.target.isConnected === false`。真实删除仍由连接中的父节点childList处理；真实背景变化、父表单变化、主题变化仍刷新，不冻结背景，不变更单一业务WebView或原生遮盖架构。

## 先失败、后修复

新增真实函数测试修前1失败/3通过：脱离节点返回深度0而非null。新增实际Shell探针在同一任务内向设置追加节点、改变class并删除；观察器交付时节点已经脱离。修前随包Debug实际发出modal-cover、modal-refresh、modal-uncover，首请求到末应答448.5ms。证据`artifacts/pixel-followup-2026-10-16/settings-detached-before`。

修后17项定向JS通过，203.40ms；覆盖脱离文本、脱离元素、连接父节点删除、正常背景变化、顶层草稿、嵌套与关闭优先、重复入口及字体重载。实际Debug与Release的脱离节点探针均记录空请求数组，未改变验收阈值。

真实仓库9标签、默认配置副本、隔离profile、随包web，无scene/pixel-exact参数。五次呈现回包前额外重复入口，仍单次呈现、单表单、depth1；延迟遮盖关闭及嵌套返回通过，网页错误0。

| 实例 | OK / Cancel / Esc / 标题关闭 / Apply后OK（ms） | 证据目录后缀 |
| --- | --- | --- |
| Debug，脱离节点与生命周期 | 571 / 237 / 201 / 240 / 260 | settings-detached-lifecycle-final |
| Release，同项 | 580 / 212 / 216 / 247 / 244 | settings-detached-release-final |
| Debug，单独分类及生命周期 | 421 / 238 / 198 / 243 / 283 | settings-detached-navigation-check |

关闭交互回归上限是2秒，571/580ms不能记为500ms达标。独立分类检查通过，四分类无呈现/捕获请求。

组合探针后紧接分类巡检的两次运行仍失败，分别3/5个捕获请求，失败JSON保留于settings-detached-fixed及settings-detached-fixed-quiet。记录显示连接中的MAIN/NAV等背景节点发生大量真实变化；首次与Release构建重叠，第二次未构建仍失败，不能完全归因构建，也不能把单独分类通过当作两次失败已解决。增强审计记录加入时间、class、region，后续应进一步定位连续开关后的背景变化。

## 构建与交付

Debug/Release构建均0警告0错误，2.88/2.89秒；两套各24个web文件与源目录逐文件哈希一致，9项共享资产、JS语法、20项PowerShell编码检查及git diff --check通过。没有改动C#逻辑，未重复全量Shell测试。

native-modal-state.js最终SHA256：DB12205FBB0AA22115243EB87DF747DDA39590E9EAD081FFA5B24EA438FF50D3。ShellModalHost仍为124BFB330AE90639AA26115380F57FF2DAA410099C11FE2AA0D1B48D6A940348。四份网页MD5在本次设置修复前后不变：mockup.js=396E779B261AC31FBE4B625181134680；mockup.css=15DE14CA11BB3E8682E403FB7EF5E054；live-data.js=F03F2393B7E03DF3B6BFFEEEBBF25987；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。其中前三项包含接续的跳转行标题修改，见同日跳转行记录。

分支dsh，HEAD为3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；既有混合修改保留，未暂存、提交或回滚。

## 启停与清理

本批实例PID6476/22368/22760/26944/26052/9076由驱动finally结束，隔离目录wR2vUz/mK1SWR/CmM8Rq/DiA7bx/dw06uF/dlQRyz由finally删除（统一前缀Augit-settings-close-）。取证JSON作为证据保留，不触碰历史被拒绝清理目录。

```powershell
# 正常入口；使用结束后正常关闭窗口。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 仅在本次进程异常时，填入启动打印的PID。
taskkill /PID <本次PID> /T /F
# 生命周期与脱离节点；脚本finally清理测试实例。
node tools/audit/check-settings-close.cjs <Playwright模块路径> <新证据目录> --many-tabs --delay-cover --delay-present --nested --bundled-web --detached-mutations
# 分类独立验证，避免把不同阶段的背景更新归因到分类操作。
node tools/audit/check-settings-close.cjs <Playwright模块路径> <另一新目录> --many-tabs --delay-cover --delay-present --nested --bundled-web --navigation
```
