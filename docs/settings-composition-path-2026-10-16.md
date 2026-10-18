# 设置闪烁：合成路径隔离与正式产物复核

日期：2026-10-16。重复设置窗口的既有排队修复保留；整体闪烁仍未解决。本批没有采纳新的产品方案。关闭GPU及DirectComposition只作诊断，未写入日常启动器；owned PNG候选已撤回，正式Debug强制非增量重建。Windows10排除本次验收，跨屏DPI未验证。

## 实机结果

均为Light、界面Microsoft YaHei UI 12、正文JetBrains Mono13、DPR1.75，普通场景入口、参考fixture与隔离profile。DDA/FFV1比较完整1820×1341、2440620像素，固定首帧、RGB容差2、排除0像素。四组源时间戳重复数均0；解码DTS量化警告保留。前景轮询均qualified=true，但不能排除采样间干扰，fullPresentationVerified均false。目录位于artifacts/pixel-followup-2026-10-16。

| 目录 | 实际合成路径 | 帧数/变化帧 | 六次刷新毫秒 | 最大采样间隙 |
| --- | --- | --- | --- | --- |
| refresh-software-composition/light | disable-gpu，软件合成及软件栅格 | 396/0 | 710.73/534.02/510.12/506.15/474.06/529.37 | 40ms |
| refresh-without-directcomposition/light | DirectComposition=false，软件合成、GPU栅格保留 | 405/0 | 643.24/583.76/549.49/533.12/577.21/559.72 | 41ms |
| refresh-without-native-occlusion/light | 关闭CalculateNativeWinOcclusion，GPU合成及DirectComposition仍启用 | 341/2 | 568.99/310.05/325.20/306.41/350.83/335.93 | 35ms |
| refresh-owned-png-probe/light | 默认GPU参数，原PNG交给无激活owned分层窗口 | 323/52 | 328.51/305.85/324.05/306.73/342.27/315.01 | 42ms |

前两组零变化是新的诊断线索，不能外推为生产修复：缺少深色、重复、真实仓库及性能/阴影完整验证，刷新多数超过500ms。第二组不能称为保留GPU合成；SystemInfo实际为gpu_compositing=disabled_software，只有rasterization等加速仍启用。

关闭遮挡计算仍在第67、68帧各变化4064像素（角131、边3933、内部0），严格门禁失败。说明该参数没有解决本次外框闪变；未将其加入日常启动。

owned PNG按用户此前授权试验：复用已保存的原PNG解码代码，改为WS_EX_LAYERED/NOACTIVATE/TOOLWINDOW的owned popup，以屏幕坐标提交，不复制DOM、不更改系统阴影。第44帧等遮盖期间外沿变化12796像素（角408、边12388、内部0），另第54帧仍4064像素变化；已查看worst-frame-44.png，矩形PNG未复现DWM外沿。全部52帧失败保留，不能只报告本样本内部零变化。候选诊断字段captureCoverTopmostChild临时改为检查owner关系，不代表真实最上层次序；未用于生产结论。

软件组shadow-regression.json的完整128px外环比较失败：两次背景不同，且外边界尚有内容变化；相同背景子集零差不能代替全外沿通过。没有阴影不退化结论。

录像SHA256按表格顺序：

- 24ac61458cb762eeffc157b121f5420ef11862495739d80400cd8e57802b0281
- 090bfd83bf6c7e0bf74f3fdaa8a2f5cd5cf75710aaa0b49a9b377ba50b6cb2f5
- 28c2afa5933030ad4c5551025565b83e48407ba5befaeffa383a40fd2f9d0c54
- 6c8afdc5a62a1a448e268321cda7cc09d7ab2a131cc511e1076c0002e1e53a8b

## 保留修改与正式复核

仅保留审计开关、互斥/类型测试及实际SystemInfo记录。默认captureBrowserArguments仍只有调试端口。三个诊断不允许叠加，软件模式与关闭DirectComposition检查实际能力；遮挡诊断强制检查GPU合成及DirectComposition仍启用。参数发出不等于证明每个内部feature命中，不把诊断记录写成正常渲染验收。

owned候选逐文件核对后恢复；ShellModalHost.cs SHA256为124BFB330AE90639AA26115380F57FF2DAA410099C11FE2AA0D1B48D6A940348。两个新增编译输入删除，候选文本留在证据目录。恢复Debug非增量构建6.75秒、0警告0错误；Release未安装候选。两项PNG负向测试113ms通过，不代表完整生命周期验收。最终11项相关JS测试185.36ms、驱动语法及共享9资产校验通过。

恢复后的真实仓库回归目录settings-restored-after-composition-probes：随包web、9标签、默认配置副本和隔离profile，无scene/DPI强制参数，延迟cover/present/close应答、重复入口、四分类、嵌套返回均通过。OK/Cancel/Esc/标题关闭/Apply后OK分别276.01/211.51/180.79/191.07/251.69ms，网页错误0。原生协议探针独立核查，不把其无DOM计时混入用户关闭耗时。此回归证明已测交互，不证明整窗无闪。

四网页MD5保持：mockup.js=396E779B261AC31FBE4B625181134680，mockup.css=15DE14CA11BB3E8682E403FB7EF5E054，live-data.js=38A6F63C55532F4D67A210E5ED933F88，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。分支dsh、HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；既有混合修改未回滚、暂存或提交。

## 清理与接续

Augit1968/10956/26112/20048/9112，采样20524/13516/16944/2500，录像25864/7600/6412/28324已退出；端口56681/56067/50376/56822/54448无监听。隔离目录f2iXl3/cf0iCE/72k182/JBOyxq/QMSXVd已独立确认不存在。候选准备前两次工具转义失败均未写文件；窗口路径转义失败后改用实际返回Window对象恢复。未启动PyCharm，未触碰历史拒绝清理目录。

后续需针对默认GPU路径的跨原生表面交接建立可验证修复；不继续仅延长等待、替换可见性属性或删除系统边框。不把软件模式的零变化作为日常入口通过。55页/全模态、终端/冲突逐行及完整阴影原有缺口保持。

日常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。停止请正常关闭窗口；异常时仅对该次输出PID执行taskkill /PID <本次PID> /T /F。审计驱动finally停止本次实例及录制器。

定向复验：node tools/audit/check-settings-close.cjs <Playwright模块路径> <新目录> --many-tabs --delay-cover --delay-present --delay-close --nested --bundled-web --navigation。合成诊断在capture-shadow-shell.cjs原有普通入口录屏参数后单独追加--software-rendering、--disable-direct-composition或--disable-native-occlusion；不得将诊断参数加入日常命令。

最终交付复核：Debug/Release各24个随包web文件与当前源码一致；正式Debug DLL不含ShellPngCover试验类型。文档声明检查和git diff --check通过；检查器仍提示历史全量快照过期，不将历史断言数列入本轮结果。
