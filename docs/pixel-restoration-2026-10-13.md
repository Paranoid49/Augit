# 2026-10-13 像素差异整改记录

> 更新至2026-10-14的接续状态见[字体与独立窗口整改](pixel-font-followup-2026-10-13.md)：JetBrains Mono已安装并随包加载，参考环境已核实为2026.2.1；独立模态已实施，Settings真实输入、拖动与返回获用户确认。有界背景刷新、Shell181项及真实六组通过，Changes19px一级缩进／16px展开画布得到局部PNG相对33px、行42px、框28×28px。下文18px解释、缺字体、宿主内模态均为首批历史状态，不能作为当前结论；完整55页同态配对、真实跨屏和Windows10仍待验收。

本批次根据[原始29项报告](pixel-difference-report-2026-10-13.md)整改当前共享网页界面。原报告、220份原始截图和控件度量均保留，未覆盖为整改后的值。新的[离线结果查看器](../artifacts/pixel-restoration-2026-10-13/index.html)展示本轮截图与几何。

已修复本次可确证的代码缺陷；**不能据此宣称Augit整体100%复原**。本机缺少JetBrains Mono，尚无所有55页与PyCharm同数据、同焦点、同DPI的配对截图；宿主内模态与参考独立窗口仍存在拓扑差异。Windows 10 22H2和真实跨显示器DPI切换未验证。

## 1. 授权、基线与实现边界

- 分支`dsh`，HEAD `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`。开始时135个已修改文件及既有未跟踪文件，未覆盖、撤回或暂存其他改动。本批次未提交混合工作区。
- 权威源码`D:/github/intellij-community@576e32820af82f97529d70f9269726803a27c016`。参考前台PyCharm为Commit、Git Log及Markdown对照页，只读查看。
- 用户明确裁决：Cancel保持同文案；复选框随UI字号放大；Amend使用蓝色last commit；Windows Stash/Reset确认在Cancel左侧。指定提交Amend属于产品外能力，不加入装饰性下拉。
- 未引入运行时依赖或改变技术栈。普通文件仍只读；Git写入、提交草稿、选择与外部刷新语义不变。
- 只通过`dsh acp`建立一个只读审查会话，未创建Codex子agent。会话已结束，客户端及临时审查输出已删除。

## 2. 29项逐项结果

| 项 | 本次处理与证据 | 结论边界 |
| --- | --- | --- |
| A01 | 子框相对分组从−24变为+18 CSS px；UI14为+23、UI40为+59，与7+11缩进逐项缩放一致 | 真实WebView2浅深六组及负向对照通过 |
| A02 | 15px中心路径对应16px外接绘制区；DPR1.75真实PNG为28×28物理px | 两主题PNG纯色边界自动核对通过 |
| A03 | 所有复选框统一24px透明画布，默认DPR1.75为42×42；框居中而非填满画布 | 画布、外框、命中与文字间距分离 |
| A04 | 浅边框#A8ADBD、深#5A5D63，使用Checkbox专用令牌 | 两主题真机PNG及状态矩阵通过 |
| A05 | 聚焦绘制区18px、2px基准内描边，对应16px中心路径的2px居中描边 | 默认及UI40焦点矩阵通过 |
| A06 | 禁用勾浅#C9CCD6、深#6F737A，深半选横线#868A91 | 状态绘制通过；不冒称所有业务禁用原因已逐页配对 |
| A07 | Clone/Stash原生checkbox与Changes共用画布、颜色和焦点 | 各自连续流程及双主题页面通过 |
| A08 | 勾形统一圆端折线，2px基准线宽；不再使用1/1.5px旋转CSS边线 | 共用遮罩几何，保留原生勾选语义 |
| A09 | 日期、设置、筛选和显示菜单共用同一复选框族 | 55页扫描与共享状态矩阵覆盖，日期交互仍沿用既有实现 |
| A10 | UI40画布78px、外框52px、描边3.25px；系统窗口控制继续16px | 六组真机度量及默认恢复回归通过 |
| A11 | Changes选中圆角基准4px；Git Log继续自己的表格规则 | 静态和实时Changes交互通过 |
| A12 | Amend和蓝色last commit分开，窄栏换行；链接复用Git Log入口 | 未实现指定提交Amend下拉，属于产品裁剪 |
| B01 | 上下布局按实际编辑标签/工具行预算，Diff保留一行正文；Changes至少一行，受限工具区局部滚动 | UI40静态和实时实际正文/列表断言通过 |
| B02 | 空间不足时提交工具区自身滚动，按钮可完整进入工具区可视范围；状态栏不覆盖其命中点 | 不缩小用户字体，不自动折叠面板；极小窗口不能同时展示全部控制 |
| B03 | 展开槽与树箭头同步缩放，SVG完全位于槽内，子项一起向右缩进 | 默认及大字号相对矩形通过 |
| B04 | 静态Diff及真实所有标签关闭槽同步缩放，SVG宽高相等 | 单击/Enter/关闭热区与背景文档保持回归通过 |
| B05 | 搜索历史触发区随命中区缩放，图形不越出搜索框；输入为图形预留内距 | 全场景几何通过 |
| B06 | 设置搜索height使用max(25px,1em+8px)，UI40为48px | 三个设置状态和实际入口通过 |
| B07 | 导航行限制于列内，长文本可换行并约束墨迹；正文保持独立区域 | 大字号设置三状态通过 |
| B08 | 面包屑使用紧凑13px图形族，UI40为42px，不再受普通52px覆盖 | 专用角色断言通过 |
| C01 | 静态与真实入口均使用dialog-xl：1641px客户区下宽1040px | 六组真机与实际打开入口通过 |
| C02 | Stash/Reset的DOM与Tab顺序均为确认→Cancel | 确认、失败、取消和键盘流程通过；Clone/Push各自保留已验证组序 |
| C03 | Commit外宽至少78px；字宽另计28px内距和6px透明边框 | 不再使用72px外盒冒充72px绘制区 |
| C04 | Cancel文案不随进行态改变，默认外宽78px；取消等待原因由局部状态说明承担 | 创建/取消/失败/重试时按钮矩形保持 |
| C05 | 模态标题可拖动，越界和宿主缩小时夹回宿主；拖动Esc不同时取消模态 | 仍为宿主内模态，独立系统窗口/系统外阴影未等价复原 |
| C06 | 信息块宽度限制于正文、文本换行、标题按1.5rem；正文滚动且系统打开动作可进入可视范围 | UI40两主题文件信息及图片错误页通过 |
| D01 | 真机字体枚举确认JetBrains Mono缺失；实际等宽仍Cascadia Mono13，恢复入口明确回退 | **同字体正文墨迹、baseline尚未通过**，不安装或分发PyCharm字体 |
| D02 | 新截图记录页面、主题、字号、来源及实际DOM；原误命名截图不再充当该页通过证据 | 旧证据不删除；截图文件名不作为页面身份判据 |
| D03 | 新回归按参考常量、父子相对位置、SVG包围盒和真实命中点验收，增加破坏缩进的负向对照与PNG栅格断言 | 内部镜像、断言数量不转换成还原率 |

## 3. 测量结果

真实宿主为本机Windows 11 x64、DPR1.75；UI12/14客户区1641×901，UI40为1024×640。界面Microsoft YaHei UI，等宽Cascadia Mono13。真机数据见`shell/geometry.json`，栅格见`shell/raster.json`。

| 对象 | 整改后 |
| --- | --- |
| UI12子项相对分组 | +18 CSS px（两主题相同） |
| UI12/UI14文件行 | 24/30 CSS px，即42/52.5物理px；保留字号档位差异 |
| UI12未选框 | 24px画布、16px外框；真机PNG画布42px，边框纯色外接28×28px |
| UI40复选框 | 78px画布、52px外框 |
| Settings真实入口 | 1040px；UI40限宽944px |
| UI40 Changes列表自身 | 88px，可在所属工具区滚动进入 |
| UI40文本编辑内容 | 141px；Diff另断言真实正文至少一行 |
| 文件信息页 | 横向留在正文内，系统打开动作可通过正文滚动完整显示 |

浏览器矩阵为55页×浅深×UI12/40，共220个页面组合，另有4个复选框状态组合。截图用于复核本次几何，不代表220份PyCharm双端像素配对。

## 4. 分批验证

| 验证 | 结果 |
| --- | --- |
| verify-pixel-remediation --all | 224个组合通过 |
| live-shell --pixel-remediation-only | 9项通过，含缩进负向对照 |
| verify-checkbox-raster | 浅深真实PNG的42px画布、28×28外框和边框色通过 |
| run-pixel-remediation-shell | 浅深×UI12/14/40六组真实WebView2通过 |
| live-shell --layout-only | 891项通过 |
| live-shell --restoration-fixes-only | 32项通过 |
| live-shell --scene-alignment-only | 30项通过 |
| verify-ux-commit-workflow | 连续交互及六组hover通过 |
| verify-ux-clone | 26组通过 |
| verify-ux-stash | 12组通过 |
| verify-ux-reset-layout | 12组通过 |
| verify-ux-push | 24组布局及3组连续状态通过 |
| Shell构建 | 临时独立输出构建成功，0警告/0错误 |
| 资源镜像/JS语法/编码/git diff --check | 通过 |
| check-doc-claims | 通过；既有2373项历史快照过期，未将其登记为本轮实测 |

阶段全页面验证的触发原因是本次修改全局复选框、图标槽和工作区布局契约。没有修改Core/Infrastructure业务规则，不重复运行全部.NET业务测试。

初次既有脚本使用旧中文界面文案、历史图标结构与旧Cancel Operation时失败；按当前英文产品规则、用户明确裁决与权威颜色更新相应断言，保留取消等待及矩形稳定检查。新几何断言还发现了Diff正文剩余高度、局部工具区横向最小内容宽度及静态/实际关闭槽的漂移，均继续修复后复测。

独立`.tmp/pixel-shell-build`产物三次未连上CDP，均清理失败实例，不计入真机通过。真机六组使用已存在的Debug外壳和显式`--web-root`加载本次源码；没有以旧网页镜像冒充新界面。临时输出目录启动问题尚未定性，不能把该构建目录的启动宣称通过。

## 5. 文件、哈希与资源清理

主要实现为`web/src/mockup.js`、`mockup.css`、`live-data.js`及两侧共享镜像；新增两份几何回归、真实外壳驱动和PNG栅格回归。修改的既有UX测试仅覆盖本次组件变化。规范与模块索引同步链接本记录。

| 文件 | 修改前MD5 | 修改后MD5 |
| --- | --- | --- |
| mockup.js | 52FC1BF4D914A7D42E549C200E01A4DF | 0009B09F3B47241E9D98AF76A7B16FA2 |
| mockup.css | 56F07FE57D6210B86B0E48AE4824BCC8 | 18466CF43BC9BF62DFB0401B340C1B0E |
| live-data.js | D68C52FA4D68C3B458CC16993677DF6A | A682619246FEB0FA82232F1063A936EF |
| bridge.js | F297BF8769E249CA0D804F4F4EEEF5A9 | F297BF8769E249CA0D804F4F4EEEF5A9 |

共享镜像修改后MD5两侧相同；未修改的current-find.js为45C1B8795D435CA5256941F3B0525545，image-preview.js为BEF0375129064DAE3C526C265B66FBE8。批次日期2026-10-13。

成功审计PID为27108、29484、13472、27536、29436、14420；失败启动PID为30928、32288、27136。对应端口及临时目录由驱动逐次输出，浏览器/CDP/外壳子树与`Augit-pixel-fix-*`目录均在finally清理。ACP客户端/结果、临时构建目录和中间工作流截图已删除。55页结果、真机证据与JSON是正式交付附件，保留用于复验；用户原有Augit、PyCharm和其他应用未停止。

## 6. 手工启动、验证与停止

在仓库根目录PowerShell执行。浏览器测试自行关闭资源；真实审计驱动只停止它启动的实例。

```powershell
# 构建并手动启动正式界面。
dotnet build src/Augit.Shell/Augit.Shell.csproj --no-restore
$pixelApp = Start-Process -FilePath './src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe' -ArgumentList '--workspace D:/github/Augit' -PassThru
# 手动停止本次启动实例及子进程；不要按进程名批量停止。
taskkill /PID $pixelApp.Id /T /F

# 当前本机已有测试依赖路径，不新增或安装依赖。
$pixelPlaywright = 'C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$pixelChromium = 'C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
$env:AUGIT_BROWSER_EXECUTABLE = $pixelChromium
node tools/audit/verify-pixel-remediation.cjs $pixelPlaywright $pixelChromium artifacts/pixel-restoration-2026-10-13 --all
node tools/audit/live-shell.spec.cjs $pixelPlaywright --pixel-remediation-only
node tools/audit/run-pixel-remediation-shell.cjs $pixelPlaywright
python tools/audit/verify-checkbox-raster.py
powershell -NoProfile -File tools/audit/verify-ui-assets.ps1
# 异常中断时只停止驱动输出的本次AUDIT_PID及其子树。
taskkill /PID <AUDIT_PID> /T /F
```

本轮未启动常驻产品服务器或内置终端。实时桩的临时HTTP服务采用随机本机端口，在finally关闭；静态55页验证直接读取本地页面，无HTTP服务。
