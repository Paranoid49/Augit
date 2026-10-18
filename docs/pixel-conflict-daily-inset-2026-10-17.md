# 冲突正常入口同尺寸配对与边界留白

2026-10-17接续。分支dsh，HEAD为3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6，接续开始562项混合修改。未暂存、提交、回滚或覆盖既有工作。Windows10排除本轮，跨屏DPI仍未验证，整体goal保持active。

## 正常入口及参考身份

实际修前conflict-daily-current、修后conflict-daily-inset均由tools/start-augit.ps1强制非增量构建，真实配置副本、默认Session profile、正常系统DPI；没有主题、字体、尺寸或DOM探针覆盖。修后PID23112、端口58181、临时仓库Augit-merge-pair-TxVA8L，构建12.77秒、0警告0错误。DLL SHA256仍为8DC72AE58C3CF37D12B6B6C598A8EE26FB822BC2BBEBBE2CF9B90D7F766EC667；本批只修改网页，不能仅凭DLL不变判断网页未变。runtime.json核对实际执行脚本、CSS及字体资源；states.json逐节点实际字符字体均为JetBrains Mono。Light、UI12、正文13、DPI168/1.75。

参考来自既有PyCharm2026.2.1 build262.9437.214，本批PID23392；专用配置的临时副本及四文件副本，不修改用户配置。原生边框调整后与实际同为1890×1215。reference-initial-clear、reference-left-clear、reference-resolved-clear均为系统原尺寸PNG，前后PID/边界/DPI稳定、活动、指针距离96px门禁通过。已目视三张参考与修前后相关实际原图，没有完成气泡或指针光晕遮挡。参考配置归档reference-environment.json；配置不能证明当前三状态的逐字形回退，旧进程布局缓存证据不外推本批。

三状态为中央base、接受左侧变ours、忽略右侧全部处理。参考没有Apply；base.txt与result.txt SHA256同为14F6DB9E5F72A87AFEB79DFF24E93B5C9E4C457CFA060134CB7C58B977B51CF5。实际驱动断言未保存磁盘。后两参考图首行光标处于闪烁隐藏相位，整行比较仍保留光标差异，不剔除后宣称同态通过。

## 完整编辑区比较

compare-conflict-lines.py新增显式--geometry daily-1890，默认legacy-1730保持旧行为。新模式必须--full-width，拒绝旧短裁框、错误尺寸、伪造尺寸元数据及未知几何；不按输入自动缩放或配准。固定边界[0,602,644,1246,1288,1890]来自实际states.json，统一用于两端，保留参考自身位置残差。编辑区y196至1127，五列各25段，共375区域、每状态1759590像素，包含编号、连接带和空白尾部。覆盖测试证明每像素恰好一次，最后一个像素变化也必须检出。

| 状态 | 修前不同像素 | 修后不同像素 | 修后相同区域/125 |
| --- | ---: | ---: | ---: |
| 初始 | 24564 | 24564 | 67 |
| 接受左侧 | 24673 | 24519 | 66 |
| 全部处理 | 25586 | 25488 | 65 |

精确RGB比较无容差。相同区域多为空白，198/375不是文字通过率。结果分别在artifacts/pixel-followup-2026-10-17/conflict-daily-lines.json与conflict-daily-inset-lines.json。两端字体栅格、槽边界、点线相位、部分底色及窗口标题/工具条仍有差异，pixelRestorationVerified=false，没有增加55页通过数。三行夹具不能代替长文本、滚动、IME、多块和全部模态验收。

## 修改依据与范围

本地权威EditorPainter.paintLineMarkerSeparator调用XCorrector.LeftAligned.lineSeparatorStart，起点为正文Insets.left；UIUtil.drawBoringDottedLine按4逻辑px周期向前取整。默认左来源18px留白对应16px点阵起点。此前syncConflictBoundaryRaster从整栏0开始画，误染左侧固定留白。两份共享mockup.js现仅为左镜像来源读取实际paddingLeft并计算点阵起点，中央、右侧及编号正文坐标不变。

新增PNG反例在修前明确失败，修后12组浅深×DPR1/1.75/2×整数/分数位置通过，继续验证完整点阵厚度、周期、空范围、滚动复用、行盒和文本。正常入口修后留白恢复；三状态正文、三栏逐行几何与修前完全一致，verification.json留存核对。参考点阵首点仍比实际右1物理px，未加入猜测性平移；已确认的留白收益不等于整行恢复。

mockup.js MD5由1186D90A02D8BC87F0F2764C13D0440F变为661C7570EB5805BB8759F2B2C6DF169F。其余保持mockup.css=6E79CBFE5FA5629AC900EAF05D109A43、live-data.js=D64AB5C4E97AA7D3ED60F456555A34AE、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。Debug随包经正常启动器更新；Release未更新，不能外推。

## 验证及资源

9项Python比较器测试通过（0.425秒），12组PNG边界专项通过，日常参数门禁1项通过（123.1507ms），JS语法、9项共享资产、git diff --check通过；未运行全量。读取测试路径和若干工具调用转义错误在执行前失败，后续更正，未算作测试通过。

参考通过Cancel正常退出，本批Explorer窗口4325664通过Alt+F4关闭；未杀Explorer shell。实际驱动finally结束PID23112并删除临时仓库及握手。2026-10-17T04:10:35Z独立检查PID23112/23392/10332无残留、端口58181/63196无监听、实际临时目录不存在。

参考临时目录C:/Users/wl024/AppData/Local/Temp/Augit-reference-size-2080ba76b5494ce5a6ec159cc9974a75仍在：此前自动审批以blocked by policy拒绝删除，本批未换通道绕过或重试，不能声称资源全部清理。原始截图、JSON及四文件作为证据保留。

## 复验和下一步

```powershell
# 正常启动；优先从界面正常关闭。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 异常时仅在核实PID属于本次实例后停止。
taskkill /PID <本次PID> /T /F
# 离线比较，输出路径必须尚不存在。
D:/Python/Python3119/python.exe -B tools/audit/compare-conflict-lines.py artifacts/pixel-followup-2026-10-17/conflict-reference-daily-size artifacts/pixel-followup-2026-10-17/conflict-daily-inset artifacts/conflict-daily-review-new.json --full-width --geometry daily-1890
```

继续优先处理设置秒级开窗、无CDP关闭与闪帧；现有环境Nahimic注入只作诊断，不能据此认定因果或性能验收。冲突下一步核对编号槽设备坐标及点阵相位，补齐当前参考实际布局字体；不能用旧反例反复试错。55页全模态、真实背景刷新首次超500ms、终端完整逐行及完整同背景阴影均未完成。远端按实机调整并保留Push URL的批准继续有效。
