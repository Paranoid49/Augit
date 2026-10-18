# 正常入口刷新状态读取整改（2026-10-17）

本批只优化现有刷新，不新增产品功能或页面。背景刷新500ms目标、透明帧、设置慢开和整体像素整改均未完成。分支dsh、HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；承接575项混合修改，不暂存、提交或回滚既有内容。

## 根因与保留修改

- `web/src/live-data.js` 的同一文件变化批次原先两次读取Git状态，文件与Git元数据同批时三次。以函数局部Promise共用本批读取，跨批重新查询；删除判断仍取本批最新状态。四项新增测试修前均失败，修后通过。
- 真实Git录像表明第二次请求原本命中宿主缓存，仅约5ms；单纯去重没有证明首轮刷新提速。不能将请求数减少宣传为500ms问题已解决。
- `GitRepositoryService` 确认仓库后并行读取裸仓库属性/公共目录；非裸仓库再并行读取根目录/未合并索引。`GitStatusService` 状态解析成功后并行读取分支与HEAD。每组统一等待所有进程结束，再按原顺序处理错误，保留裸仓库、链接工作区、未出生分支、detached HEAD及取消规则。
- 没有新依赖，不新增缓存或修改Git写操作。成本是每组短暂同时运行两个只读Git进程；沿用30秒超时、输出边界和取消清理。并行候选在两批正常入口均降低状态读取耗时，故保留。

## 正常入口证据

全部实际侧经 `tools/start-augit.ps1` 非增量构建，真实设置副本、默认Session、未强制DPI。真实临时仓库中修改40行文件，当前文件及跳转行保持打开；未注入刷新DOM标记。附CDP、录屏及只读请求计时，不作为无CDP性能验收。证据根目录为 `artifacts/pixel-followup-2026-10-17/`。

| 目录 | 首轮刷新观察上界，ms | 每批Git状态读取，ms | 结论 |
| --- | --- | --- | --- |
| refresh-status-before | 519 / 398 / 413 / 425 | 非Git，合计约4–5 | 无仓库对照，不外推Git场景 |
| refresh-status-git-before | 700 / 617 / 588 / 610 | 203.7+4.8 / 201.8+4.7 / 189.2+4.7 / 215.8+6.0 | 修改前两次请求 |
| refresh-status-git-after | 741 / 588 / 607 / 588 | 202.3 / 191.8 / 203.8 / 197.3 | JS去重后一次，首轮耗时无明确收益 |
| refresh-status-git-parallel | 622 / 541 / 595 / 563 | 140.5 / 137.5 / 151.6 / 138.7 | 只读命令并行后 |
| refresh-status-git-mapped | 624 / 524 / 553 / 597 | 144.0 / 131.8 / 141.2 / 143.4 | 最终加载身份确认，仍全部超500ms |

并行阶段文件变化约60ms后开始Git查询，约193–205ms开始读取正文；遮盖请求176–256ms、背景刷新117–123ms，撤盖请求在508–603ms。阶段存在重叠，不能简单相加；下一步主要剩余成本在遮盖捕获与背景呈现，不是重复状态请求。四次样本不能外推完整性能分布。

并行阶段PID28448，端口61489。`process-identity.json`核对命令行与exe路径；`mapped-images.json`通过只读VirtualQueryEx/GetMappedFileName核实本实例实际映射路径，再对对应磁盘文件取哈希：

- Shell `Augit.dll` SHA256：62BE2A9637AC367090EC62CE0DEB322F83F70935A4C36EFB145A31A2FFD6958F。Shell未改，哈希不变不能证明Infrastructure未更新。
- `Augit.Infrastructure.dll` SHA256：1F4F34550F6D601C9850A4F068BD03F9AC85A44347BF058DF2FBB37FFBC65E80，来自Shell的Debug目录。
- `AudioDevProps2.dll`仍实际映射；未确认其因果，按现有性能规范仅作诊断数据。未修改系统注入或进行提权。
- `runtime.json`：真实执行live-data.js SHA256为330201D74E92182A104B44F7020782F1F97E9CE6D1EC56AC904085358AFA417F；DPR1.75，1646×981网页视口，界面12、正文13，实际字形JetBrains Mono。没有打开终端，终端字体空采样不作为证据。

中间 `refresh-status-git-identity` 因Windows模块枚举未返回托管Infrastructure而主动失败并清理，events为空，不计验收。新增映射扫描后在新目录复验，未覆盖失败证据。映射路径证据不是进程内存字节哈希。Release未更新。

## 完整帧结果与边界

五份录像逐帧比较完整585×252矩形，保留边框、圆角和光标，不裁掉异常后宣称通过。帧数依表顺序273 / 325 / 328 / 313 / 303，最大帧间隔46 / 41 / 32 / 45 / 43ms。

该阶段303帧最大差异265像素：78为输入光标区域，166在外侧3px带，另21在左圆角附近，完整异常仍保留。修前真Git和并行后均有187个非光标变化像素；没有证明边缘闪动解决。目视最差帧未见整块白窗，不能推断采样间无透明帧，也不代表其他模态通过。完整统计、原始录像及最差PNG均在各目录；区域归因见 `refresh-status-frame-regions.json`。

## 分批验证

- 18项JS：刷新渲染11、队列交付3、真实文件审计4，通过（1.15秒）。审计覆盖原始桥接参数/接收者/返回值、乱序及未完成请求、真实Git夹具内容和范围；真实夹具测试finally清理。
- 最终Infrastructure定向25项通过（约2秒）：仓库、状态、命令读取器。新增真实并行进程取消测试等待两个PID落盘后取消，返回时确认两进程均退出；finally再次等待清理。
- 直接依赖ShellBridgeGitStatusTests 2项通过（约1秒）。没有重跑全量测试。
- 映射路径3项、完整帧分析器3项Python测试通过。初版映射测试的反斜线在传输中丢失，1项失败后修正并复验；初版C#测试属性名误写亦已修复，不隐去这些失败。
- 相关5份C#格式验证、JS语法、9共享资产、Debug25资源镜像、22脚本编码及git diff --check通过。文档声明检查通过，但仍明确提示旧全量快照过期，不能据此计全量通过。

四网页MD5：mockup.js=661C7570EB5805BB8759F2B2C6DF169F；mockup.css=D671A6BA32C439BD4CBD774862FB084C；live-data.js由D64AB5C4E97AA7D3ED60F456555A34AE变为C2DE269FA473CF347657C93F0B064E70；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

## 清理与复验命令

`refresh-status-cleanup-check.json`独立核查六批PID、录屏进程、五个前景监视器、监听端口和六个临时目录，均无本批残留；无Augit/ffmpeg/ffprobe/python运行。本次未启动服务器。之前参考采集目录删除被拒绝的事项仍保留，未绕过重试，不能被本批清理结果覆盖。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 优先正常关闭；异常时只停止本次核实的PID及其子进程。
taskkill /PID <本次PID> /T /F
node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新的证据目录> --git-fixture
dotnet test tests/Augit.Infrastructure.Tests --no-restore --filter 'FullyQualifiedName~GitCommandRunnerTests|FullyQualifiedName~GitRepositoryServiceTests|FullyQualifiedName~GitStatusServiceTests'
node --test tools/audit/refresh-daily-file.test.cjs tools/audit/workspace-change-render.test.cjs tools/audit/workspace-change-delivery.test.cjs
```

审计的Python解释器可由AUGIT_AUDIT_PYTHON指定，当前为D:/Python/Python3119/python.exe。所有原始失败证据保留，不复用已存在输出目录。

## 未完成项

正常入口设置秒级慢开及无CDP完整开关、背景透明/边缘帧与500ms、55页全模态同态、终端/冲突完整逐行、系统阴影四角均未结案。Windows10排除本轮，跨屏DPI仍未验证。本批不增加55页合格数，goal保持active。下一步依据实际时序定位遮盖捕获和背景呈现，不能重复已否证的裁切捕获、内缩、预创建、换图重叠等候选。


## 后续根因与计时纠正（本节覆盖前述阶段快照）

只读请求序列发现首轮撤盖后立即再次遮盖。`refresh-status-git-late-mutation`记录证明，首次背景捕获开始后提交详情的changed-files/commit-detail发生真实DOM更新，第二链不能直接丢弃。

查到明确身份错误：`refreshCommitDetails()`以完整revision判断缓存，`loadCommitDetails()`却把短显示hash写入已加载身份。同一提交因而总不能命中，状态首次变化时又读取详情，造成第二轮背景捕获。现改为写入宿主契约已有的`commit.fullHash`；不改变详情内容、分段渲染和真实提交切换。新增两项修前失败/修后通过，另补相同短前缀不同完整提交仍重新读取，共3项通过。

审计旧`observedMilliseconds`只证明首轮新图和撤盖，不能称完整刷新上界。现保留该旧字段以便追溯，同时在既有400ms静置窗口后等待所有已发出的遮盖/刷新/撤盖请求结束、后台busy解除，再记录最后一次撤盖的同源单调时钟。静置并不能保证未来永远无更新；所有请求和DOM变化另落盘，不把静置等待本身加进产品耗时。新增回归拒绝第二链未结束、最后操作不是撤盖和空请求。

| 正常入口目录 | 首轮观察，ms | 最后撤盖，ms | 遮盖次数 |
| --- | --- | --- | --- |
| refresh-status-git-settled（身份修正前） | 634 / 551 / 538 / 529 | 919.6 / 535.6 / 521.1 / 515.8 | 2 / 1 / 1 / 1 |
| refresh-status-git-detail-fixed（修后） | 660 / 532 / 579 / 538 | 631.1 / 518.7 / 569.1 / 502.9 | 1 / 1 / 1 / 1 |

首轮第二链消除，最终一次样本约631ms而非原919.6ms。后续三次仍超500ms，波动存在，不宣称全面性能达标。修前修后均按正常入口强制构建、真实配置副本、默认Session、DPR1.75、UI12/正文13。最终实际PID18760/端口50178；映射Shell及Infrastructure身份与上文相同；实际执行live-data.js SHA256=B888B4E78E267276BA587CB7329434431DC0CFCB1A347C4AE5BCCF1B96303DE3，MD5=DE85823861C020608F28DE997197B555，覆盖上文C2DE…阶段。其余三网页未变，Release仍未更新。

最后三份原始录像分别314/313/319帧，最大间隔31/40/39ms，最差均265像素，含光标与边缘；修后最差PNG已目视，仍不能判无闪。完整矩形没有裁去边缘。最终22项JS组合通过后，再补同短前缀测试运行3项详情测试通过；本批JS合计23项覆盖。Infrastructure25、桥接2及Python6项沿用前述定向结果，未无故重跑。最终Debug25资源镜像和git diff --check通过。

后续三批7052/22428/18760、各录屏进程、监视器27236/13636/28192、端口和临时目录独立核查均清理，见`refresh-status-late-cleanup-check.json`。加前述六批共九批清理完成，不覆盖历史拒删参考目录。新增审计工具与测试均保留中文注释；没有提交。

下一步仍需降低真实遮盖/捕获成本并解决边缘变化；旧裁切、预创建、内缩、重叠已否证不得重做。设置慢开、55页全模态、终端/冲突逐行及完整阴影目标继续active。

## 仓库身份组合查询候选（已撤回）

将absolute-git-dir、is-bare-repository及git-common-dir合入一次rev-parse的候选已通过22项仓库/状态及2项桥接定向测试，但正常入口实测没有稳定收益，因此撤回组合参数及专用解析器，保留独立Git目录、中文空格路径和子目录工作区的真实回归。恢复后仓库9项通过，未重复全量。

正常启动证据目录为`artifacts/pixel-followup-2026-10-17/refresh-identity-combined`，PID14864、端口56768、真实配置副本与默认Session。四次首轮629/575/517/546ms，最后撤盖615.4/560.8/505.4/537.9ms，状态145.1/143.1/135.9/148.2ms；不能认定较既有并行实现提速，仍超过500ms。候选映射Infrastructure SHA256为E5239D0AA792E2EFBC932F74D332FEEA389EE23C6ADAF70E698082F2B0F1D149。

完整585×252录像309帧，最大帧间隔45ms，RGB容差2下最差187像素、范围[1,1,165,252]，原始录像SHA256为415597FC9890CF4482FDA55D37BACF11412669E3257BB1300D8B7E8796514B5C。保留全矩形和最差PNG，不据此宣称无闪。分析命令为`D:/Python/Python3119/python.exe -B tools/audit/analyze-static-modal-video.py artifacts/pixel-followup-2026-10-17/refresh-identity-combined`；已有输出不得重复覆盖。

撤回后经`tools/start-augit.ps1 -PrepareOnly`强制非增量构建，0警告0错误、5.27秒。仓库服务源码SHA256恢复4B6115EBF504A3966391DF11D44D8CF8AF735736C04D5C59ADF840638037D048，Debug Shell与Infrastructure分别恢复62BE2A9637AC367090EC62CE0DEB322F83F70935A4C36EFB145A31A2FFD6958F及1F4F34550F6D601C9850A4F068BD03F9AC85A44347BF058DF2FBB37FFBC65E80。四网页未改，Release未更新。独立清理复核`independent-cleanup-check.json`确认PID14864、监视器22192、端口56768及Augit-daily-refresh-lbjG04目录均无残留。历史拒删参考目录未触碰，整体未完成。
