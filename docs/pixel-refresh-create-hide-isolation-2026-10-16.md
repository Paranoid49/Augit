# 背景刷新创建、显隐与表单隐藏隔离（2026-10-16）

## 结论

前一批排队重开修复属于实质进展，正式实现仍有闪帧。本批将创建截图层、显示遮盖、隐藏表单三步分离：只创建隐藏截图层和只显隐遮盖的样本均无变化；加入真实表单隐藏后复现整面露底。把opacity改为visibility的候选仍失败，已恢复。没有新增保留的产品修复，不宣布背景无闪或整体目标完成。55页/全部模态、终端/冲突逐行及完整系统阴影的原有缺口保持；Windows10排除、跨屏DPI未验证。

## 方法

沿用单一业务WebView和原始PNG遮盖，不复制DOM、不改变DWM边框。四组均普通场景、Light、UI12、DPR1.75、参考fixture与隔离profile，computer-use确认实际主窗前景后释放检查点。每组从零字节、零遮盖句柄开始。DDA/FFV1保留1820×1341完整表面，首帧固定，RGB容差2，排除0像素；源时间戳无重复，解码DTS量化警告保留。未筛除鼠标、角点或失败帧。

第一组仅在临时Debug里省略遮盖显示，完成控制器创建、PNG加载后保持控制器及子窗隐藏。审计直接调用cover，不让产品隐藏表单；状态断言遮盖不可见且PNG字节数大于0，背景版本保持0。第二组恢复正式C#实现，同样不隐藏真实表单，只执行cover、保持200ms、uncover。第三组正式实现中执行既有hide-only分支，遮盖就绪后隐藏表单200ms并恢复，没有modal-refresh或背景换图。前三组的200ms为诊断保持时间，不计产品性能。

第四组仅将两份共享CSS的data-native-background-capture从opacity:0改为visibility:hidden，运行完整真实背景变化、捕获及换图，不叠加其他候选。四组原图、录像、清单及分析位于artifacts/pixel-followup-2026-10-16下表所列目录。

| 目录 | 模式 | 帧数/变化帧 | 最坏变化像素（角/边/内部） | 最大源帧间隔 |
| --- | --- | --- | --- | --- |
| refresh-create-only-probe/light | 只创建隐藏截图层 | 345/0 | 0 | 39ms |
| refresh-unchanged-cover/light | 正式遮盖显隐，表单不变 | 325/0 | 0 | 37ms |
| refresh-hide-only-current/light | 正式表单隐藏恢复，不捕获背景 | 384/1 | 2295795（660/86715/2208420） | 34ms |
| refresh-visibility-hidden-probe/light | visibility候选完整刷新 | 353/3 | 2295795（660/86715/2208420） | 40ms |

第三组第72帧、第四组第113帧已目视：Settings内部直接露出主界面的文件正文和Git区域。第四组另有第70/71帧各9717像素边缘变化。因此问题不能仅解释为系统边框颜色，也不能简单把opacity换成visibility。前两组零变化只证明所采样条件，不能排除偶发性或证明全场景无闪；所有fullPresentationVerified仍false。

前景采样第一至四组分别101/105/110/107次，最大间隔95/100/93/102ms，均qualified，无观测干扰；不能排除采样间干扰。前三组总观察时间包含200ms保持，不与正式耗时比较；第四组完整刷新595.99/380.34/387.12/497.63/395.24/430.08ms，首次超500ms。

录像SHA256按表格顺序：

- 76f74861ee9b2a057df0992a7503229ed190c0dbbec01e64f4834e2c07112d57
- 4e4b14a00141801d2ac27277a53d7a74e4d66009b196fd97b614604095f0a1f6
- b91e65bb729b9d2e89b70c9b945308fbf7e7285388cd9a357b962c300192efba
- bef50bd9cd14535bf1f643d52885d8aa31dd44103c031b42a022b2dda7ff124b

## 保留的审计改进

refresh-cover-isolation.cjs增加exerciseUnchangedCover，核验PNG存在、创建隔离不显示、正常显隐必须就绪、背景版本不变和finally撤盖。新增3项测试，加原有隔离及身份测试共12项通过。capture-refresh-screen增加明确的isolationMode，保留基线资源身份，拒绝混用互斥模式。--isolate-create-only只供保存的临时诊断源码使用；正常产品会被断言拒绝，不能将失败绕过当成功。--isolate-unchanged-cover可直接复验正式产品。

采样时的第一/二组清单保存了before与after，准备状态通过运行时断言核查；之后审计返回值进一步增加prepared，未来采集可直接审计中间状态。本批旧清单没有回填或伪造prepared。

## 恢复与清理

ShellModalHost在核对当前候选字节后恢复SHA256为124BFB330AE90639AA26115380F57FF2DAA410099C11FE2AA0D1B48D6A940348，恢复非增量构建0警告0错误6.18秒；诊断构建5.44秒。CSS候选在两份文件均匹配后逐字节恢复，未进入随包产物。Debug/Release各24项web镜像一致，共享9资产一致、语法与git diff --check通过。没有最终C#产品变更，不重复全量Shell套件。

四网页MD5保持：mockup.js=396E779B261AC31FBE4B625181134680、mockup.css=15DE14CA11BB3E8682E403FB7EF5E054、live-data.js=38A6F63C55532F4D67A210E5ED933F88、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。既有混合修改未回滚、暂存或提交。

Augit11836/23892/2052/5616、前景28580/6608/27968/14544、录像13164/26340/27704/26076已退出；端口49193/61164/59418/53917释放。隔离目录AM1MtF/czfVP6/W0O9to/vMJGsV由finally删除并独立确认。没有启动参考PyCharm，没有触碰历史被拒绝的清理目录。诊断基线、候选文本和原始证据保留在artifacts，作为可复验成果，不是运行资源。

## 后续与启停

下一步应在表单隐藏与恢复各阶段记录真实原生兄弟顺序和遮盖可见性，配准录像，检验业务表面变化后遮盖是否仍在最前；不能把窗口句柄排序等同于DWM实际提交，也不能仅增加等待、移除系统阴影或缩小验收区域。

```powershell
# 日常启动，停止请正常关闭窗口。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 正式产品的表单不变对照；激活主窗后释放main-active-ready。
node tools/audit/capture-shadow-shell.cjs <Playwright模块路径> <新目录> Light --normal-entry --refresh-video --desktop-duplication --repeat-static-frames --activate-main --isolate-unchanged-cover
D:/Python/Python3119/python.exe -B tools/audit/verify-refresh-surface.py <新目录> --require-stable
# 驱动finally自动清理；异常只停止本次输出的PID及其子进程。
taskkill /PID <本次PID> /T /F
```
