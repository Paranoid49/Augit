# 设置接续：源码恢复与运行产物错位（2026-10-16）

## 本批结论

确认并修复一个启动交付缺陷：恢复源码保留旧修改时间时，增量构建可以成功但继续运行较新的试验DLL。日常入口现在强制重新编译，日常入口审计取消跳过构建。Debug、Release已重新编译并核对不含本批诊断标志。重复设置入口、五条关闭及嵌套回归通过；正式产物仍有内部露底和边框闪动，用户报告尚未全部解决。

不能据本次发现断言所有历史截图差异均由旧DLL引起，也不能再以历史源码恢复哈希加增量构建成功作为产物恢复证明。未逐份追溯历史二进制，历史试验的实际运行身份需重新核验后才能用于排除方向。

## 直接证据

本批先在ShellModalHost中加入仅诊断用的三段750ms等待，分别隔离空子窗、隐藏控制器图片就绪、子窗显示。第一次Task.Delay续体离开窗口线程，采集失败；改为窗口线程WebMessage回调完成后采到395帧GDI画面零变化。但该试验增加人为等待，且只隐藏表单、不替换背景，不能代表正常刷新无闪或延迟达标。诊断消息也不属于产品协议，不保留在产品。

随后按候选哈希核对并复制基线恢复，ShellModalHost.cs恢复SHA256为124BFB330AE90639AA26115380F57FF2DAA410099C11FE2AA0D1B48D6A940348。增量构建成功，但真实仓库回归出现audit-cover消息解析错误及modal-cover超时。检查DLL确实仍含audit-cover-phase:标志。源码修改时间为2026-10-16 06:38:47 UTC，DLL为06:47:09 UTC；复制保留了旧时间，构建跳过编译。强制非增量编译后标志消失，同项回归通过。

证据分别保留在artifacts/pixel-followup-2026-10-16/refresh-controller-phases、settings-production-check、settings-rebuilt-production。失败记录不删除、不改成产品正常通过。

## 保留修改

- tools/start-augit.ps1：普通启动使用dotnet build --no-restore --no-incremental /nodeReuse:false，构建失败仍阻止启动旧程序。代价是每次普通启动完整编译，本机本批约6秒；不改变窗口、字体、DPI和会话设置。
- tools/audit/daily-launch-command.cjs：日常入口审计不再传-SkipBuild，与用户命令执行相同构建路径。
- tools/audit/start-augit-build.test.ps1：实际调用启动器并截获构建参数，覆盖强制编译、禁止节点复用、显式跳过及失败中止。沿项目PowerShell约定使用UTF-8 BOM及LF，避免Windows PowerShell误读中文。
- docs/development-validation.md：明确恢复试验后非增量构建与产物核验要求。

## 验证

启动构建测试、原有配置保留/迁移测试、两项日常命令测试、三项设置入口/字体测试通过。21项PowerShell编码与解析、共享9资产、Debug/Release各24项web镜像、JS语法、git diff --check通过。没有产品C#逻辑保留，未重复运行全量Shell套件。

正常入口实际构建后启动成功，使用真实仓库和配置副本，未加入场景、DPI或会话重置参数；证据daily-rebuilt-entry。真实随包Debug组合回归使用9标签、延迟呈现应答、重复入口、延迟遮盖、四分类、脱离节点、真实文件两次变化及嵌套返回，网页错误0，五条关闭save/cancel/escape/header/apply-save为275.87/246.60/232.10/222.49/263.62ms。此项不等于逐帧视觉验收。Release本批仅强制构建及资源核验，未实机重跑同套。

Debug DLL SHA256：277BA1919567F3A2DC04405975908CDA7858097E2BBC542EC67393E4790E7C43。
Release DLL SHA256：962CEE99A02250BDB02E04FD86ADC5DFB41961EB61A0E7F244B7822F56622A35。

## 正式产物仍有闪动

refresh-rebuilt-production为撤回全部诊断并强制编译后的普通入口、Light、DPR1.75、UI12、JetBrains Mono13。DDA/FFV1共348帧，每帧1820×1341、2440620像素，首帧固定、RGB容差2、不排除任何像素。4帧变化：62/63帧各2295787像素，其中内部2208412；74/75帧各4061像素，仅边缘/圆角。frame-62.png已目视确认显示底层文件及Git区域，确属内部露底。首次575.44ms，后五次325.28–356.28ms。严格验收退出1。

录像SHA256为444e3ebecfd1b0a528776b68b9a77aacb4c56acb5bc6a5addc1987c0f86df5f4。源时间戳重复0，最大间隙35ms；解码DTS量化警告保留。前景102次采样无观测干扰，最大间隔97ms，不能排除采样间干扰。sampledWholeSurfacePassed=false、fullPresentationVerified=false，不将GDI分段样本零变化推广为DDA正式流程通过。

四网页MD5未变：mockup.js=396E779B261AC31FBE4B625181134680；mockup.css=15DE14CA11BB3E8682E403FB7EF5E054；live-data.js=AD3BA8F7851F463ABF99FF932B4CA95A；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

## 清理及启停

本批Augit PID3648/1480/23172/6128/27868/27420、前景及录像PID28668/12696/10604/26096/12576/18384均退出；56500/58210/60503/51529/65243/58337无监听。隔离目录E0h6zr/0zjFC0/FqKYbT/T4BtKN/0HbgAu/Se8fd8及检查点已清理，原始证据保留。没有回滚、暂存或提交既有混合修改。Windows10排除，跨屏DPI未验证。

正常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。正常关闭窗口停止；异常仅针对本次启动输出PID执行taskkill /PID <本次PID> /T /F。不要为省略构建时间使用-SkipBuild来验证源码整改是否生效。

下一步应基于本批已核实二进制，处理背景捕获期间的遮盖失效与原生边框合成，不能只依据状态机、静态截图或延时诊断宣布无闪。55页、全模态、终端/冲突逐行与完整阴影目标仍未完成。
