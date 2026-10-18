# 终端基础行格与相同字形配对（2026-10-15）

整体整改仍未完成。上一批格宽整改已经取得真实13号字两行横幅等宽。本批补齐基础行格：默认13号字从约17.18改为18逻辑px，17号字从约22.25改为24逻辑px。17号字原始双端截图的九类相同字母行步长均42物理px，两行宽度与高度一致；13号字仍有1物理px行步长差，不计完整终端、抗锯齿或55页同态验收通过。

## 依据与修改

用户此前已裁决按PyCharm实机行高整改并同步规范。参考设置仍为JetBrains Mono、Line height1.0、Column width1.0。CharacterGrid使用EditorView网格，EditorView.java:991–1018在行距1.0时返回字体基础高度；随包2.304字体hhea为1020/−300，基础高度为ceil(size×1.02)+ceil(size×0.3)。现有32字号×8比例独立JBR数据256组均吻合：13号字18、17号字24逻辑px。公式不含编辑器的1.2行距。

xterm使用浏览器字体基础度量且先取整设备字符高度，原默认13/17号字实际约17.18/22.25px。已在前批确定性DOM补丁中增加augitCellHeight，指定同一字符网格的逻辑高度，影响行、选区、光标、fit与鼠标行命中；0恢复上游路径。仅实际加载的随包JetBrains Mono启用；字体切换、字号/DPI变化与加载完成沿现有刷新入口。lineHeight仍为1.0，无新增外观设置或依赖。

共享mockup.js/CSS同步静态终端基础高度，消除17号字静态23.5385px而实时24px的差异；其他字体/加载失败使用normal，不套用随包字体常量。规范及第三方说明同步，不改正文、Diff、冲突、工具栏高度。

## 不能用不同字形行顶判断行距

新增measure-terminal-row-step.py，配对自然两行横幅中共同出现的d/e/h/i/l/n/o/r/s；逐字符读取原PNG，公布采样格、上/下界和中位步长。整行Windows PowerShell与Copyright的最顶部由不同字形决定，不能直接当作基线步长。

证据根目录artifacts/pixel-followup-2026-10-15/terminal-row-grid。全部PNG为CopyFromScreen原始物理像素，175%DPI，浅色自然Windows PowerShell横幅，未发Shell命令。裁切图只用于展示，测量始终读原图。两端整体布局不同，只计局部配对。

| 样本 | 相同字形行步长 | 第一行宽×高 | 第二行宽×高 |
| --- | ---: | ---: | ---: |
| PyCharm13，前批及本批恢复后 | 31 | 249×18 | 789×23 |
| Augit13，修前 | 30 | 249×18 | 789×23 |
| Augit13，修后 | 32 | 249×18 | 789×23 |
| PyCharm17 | 42 | 321×24 | 1016×30 |
| Augit17，修后 | 42 | 321×24 | 1016×30 |

13号字逻辑18px在175%对应31.5物理px；本批一组相邻行不足以证明长期舍入分布与参考相同。修后32对31仍失败，不能因逻辑高度相同就宣称像素通过。17号字24px对应42物理px，九类相同字母的上/下界步长均一致，证据强于整行顶部接近；但两端绝对位置、字形抗锯齿与完整多行混排仍未通过。

核心证据：row-pair.json记录参考13/修前13/修后13；reference-17-step.json和capture-17/row-step.json记录17号字；reference-13-restored-step.json证明参考恢复13后仍为31。reference-17-ink.json与capture-17/banner-ink.json记录墨迹宽高。

| 原图 | SHA256 |
| --- | --- |
| capture/actual-light.png | 830cb22fbe7f20650bfaae1a51f7d868c9be9ca32611d08c521a61b4229bf8cc |
| reference-17.png | c0026c94da0ba9b1cfe15fb0478bd5b917fd2a8ec26819758fb9d64185421197 |
| capture-17/actual-light.png | 9cdb16bf042de131f37b454d8def5247696eddd79d3cc27afa2484f8831c15f9 |
| reference-13-restored.png | 64a54c85ee30b9c657343ffd88a3a3e2769c03863c1b1a2df8a3b184c75ef1e4 |

## 验证与未通过项

- 48组合浏览器矩阵通过：6模拟DPI×4字号×双主题，包含行高、选区高度、首/第三行鼠标命中、网格/字距与光标；6组100列光标偏移小于0.04物理px。模拟DPI不等同跨屏实机验证。
- 256组字体高/宽独立数据、未加载/其他字体回退、确定性补丁/原包漂移拒绝3项Node测试通过。相同字形分析3项Python测试通过0.064秒，覆盖混合字形行顶不同、空白/越界和无共同字符。
- 真实WebView2主题深浅、取消、13→17→13、JetBrains Mono→Consolas→JetBrains Mono及ANSI16项通过。最终静态/实时13为18px、17为24px，复用原终端节点。参考仅改隔离环境字号17取证，随后通过真实设置恢复13并取得原图。
- Debug构建0警告0错误2.87秒；未跑全量套件。共享9文件与Debug网页24文件镜像通过。
- 一次静态脚本编辑因引号失败未写入，改为定点补丁；两次本地检索表达式错误已改用准确路径。一次参考启动工具字符串转义失败未启动进程，修正后仅启动PID22196。17号Augit驱动在尝试激活前已完成并自行清理；重新查询确认退出、检查原图及严格前景元数据成功，没有重启或伪造截图。
- 13号字相同步长未通过；参考终端还有输出块/提示符分隔结构，整窗布局、全输出、暗色同态和其他字号实机配对仍缺失。背景刷新、冲突、阴影及55页/全模态总体目标仍保留；Windows10排除本次验收，跨屏DPI未验证。

## 哈希与资源

运行时xterm489086→489227字节，最终SHA256 bdee3ebae10716807374a2b385f1803389011dd89562aee33919e8d53623c0d3；七个唯一锚点，原包和MIT许可证保留，锁文件同步。

| 文件 | 修改前MD5 | 修改后MD5 |
| --- | --- | --- |
| mockup.js | DB12568D024253582D1C09B5DA1CD5AE | 3B3399F68FD796B48952022625C3B5F4 |
| mockup.css | 6AFA43AB3714BA447FEA02CBA3A33274 | 09D3687D25A62F2377D117F3AE78599F |
| live-data.js | 101590DDBA634002223B3F9CD4EB89A9 | B48E37B755997A2C28D98AF8CB87E7ED |
| bridge.js | F297BF8769E249CA0D804F4F4EEEF5A9 | 不变 |

分支dsh、HEAD3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6，保留混合修改，未暂存/提交/回滚。Augit9500/9132/10472/18424及子树已退出，51832/52995/59217/58228无监听；BwpHqK/FRwpA4/UzUSSb/Nix60o临时profile均不存在。参考22196及子树taskkill完成，随后按路径再次查询无残留。浏览器矩阵finally关闭全部context/browser，无临时服务器；Python临时图片用TemporaryDirectory自动删除，正式证据保留，未重试历史拒绝清理。

~~~powershell
node --test tools/audit/terminal-geometry.test.cjs
python -B tools/audit/test_terminal_row_step.py
node tools/audit/verify-terminal-grid.cjs <Playwright模块路径> <Chromium.exe路径> artifacts/terminal-grid-next.json
node tools/audit/live-terminal-palette.cjs <Playwright模块路径> artifacts/terminal-live-next
node tools/audit/capture-terminal-pair.cjs <Playwright模块路径> artifacts/terminal-17-next --font-size=17 --wait-foreground
# 45秒内激活本次窗口；原图仍严格校验前景，不合格拒收。驱动finally自动清理。
taskkill /PID <本次Augit_PID> /T /F
# 参考启动沿整改接续记录§7的隔离PYCHARM_PROPERTIES；本批停止命令：
taskkill /PID 22196 /T /F
~~~

收尾：7个相关JavaScript语法、确定性补丁校验、9项资源锁大小/SHA256、文档声明检查、git diff --check通过；当前路径无本批参考进程或Playwright浏览器残留。原全量live-shell快照仍过期，不以本批定向结果替代。

2026-10-15终端相位分析：新增JBR/浏览器20行重复字形夹具，8份样本最大累计漂移0.5物理px；实机0.25/0.5px文字偏移改变两行步长，但基线差补偿和原点取整均未稳定修复，因此未写入产品。4项分析器测试及真实取证通过，完整配对仍未完成。证据、失败与启停清理见[终端相位记录](pixel-terminal-phase-2026-10-15.md)。
