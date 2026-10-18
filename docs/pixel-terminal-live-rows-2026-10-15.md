# 终端八行实机步长接续

日期：2026-10-15。用户已排除本次 Windows 10 实机验收；跨屏 DPI 保留未验证。本批仅补充终端原始截图分析，不修改产品行高、字形位置或当前规范。

## 结论与边界

PyCharm 2026.2.1 与 Augit 在 DPI 168、JetBrains Mono 13 下，通过真实 PowerShell 执行 `1..8 | ForEach-Object { 'Ag 0123456789' }`。八行首字母 A 的七个纵向步长两端均为 `[32,31,32,31,32,31,32]` 物理像素；相对 31.5 物理像素理论步长，累计偏差最大均为 0.5。当前样本没有累积行距漂移，不能再仅凭不同两行的 32/31 差值判断基础行格错误。

参考行顶为 `[1271,1303,1334,1366,1397,1429,1460,1492]`，实际为 `[1293,1325,1356,1388,1419,1451,1482,1514]`，差值均为 22 物理像素。两端窗口、终端原点和面板布局不同，此差值不能用作产品固定平移量。默认 18 逻辑像素基础行格不变。

完整终端配对仍未通过：实际截图第三、四行中部存在鼠标光晕，首字母采样带没有遮挡；浏览器 hover 不会可靠移动系统鼠标。目录名称 actual-clear 不代表无遮挡。两端原图大小分别为 2430×1637、2450×1628，不能计入 55 页同态合格配对。按行顶局部对齐后，每行 13×17 首字母区域仍有 186/220/215/221/221/221/221/221 个 RGB 不同像素；此项包括背景与抗锯齿，不是纯字形误差率，不证明整行或整窗已经复原。

## 原始证据

- 根目录：`artifacts/pixel-followup-2026-10-15/terminal-live-rows/`。
- 参考：`reference-eight.png`，SHA256 `6190d1e91021b8006b0dedc31232098c84f1e1da911ab34a8d1fe1cd457d12bb`。
- 实际：`actual-clear/actual-light.png`，SHA256 `e6a3da56b25cf10b3569388362ddf917890fcad52580e1091400b14a93185c2f`。
- 量化：`eight-row-comparison.json`，包含原图哈希、采样区域、逐行位置、元数据及限制；`qualifiedPair` 和 `pixelRestorationVerified` 均为 false。
- 原始 PNG 未缩放、未重采样；前后前景身份、进程、DPI、边界检查通过。前景检查不替代无遮挡审查。
- 实际终端 buffer 恰好包含八行相同输出，前端错误为空，保留自然启动横幅。

## 修改与必要验证

`capture-terminal-pair.cjs` 增加可选 `--repeat-rows`，通过终端键盘执行固定命令并等待八行真实输出，不向渲染 buffer 注入内容。默认仍采集自然横幅。`measure-terminal-row-step.py` 增加原始 PNG 内的上下采样边界，避免生成裁切图后丢失原图哈希。`test_terminal_row_step.py` 覆盖区域边界、原图哈希保持、行数不符和越界拒绝。

验证命令：

    python -B tools/audit/test_terminal_row_step.py
    node --check tools/audit/capture-terminal-pair.cjs
    git diff --check

四项分析器测试通过；两次真实采集均完成，并保留一次仍有光晕的复采事实。一次分析因 Windows 默认 GBK 解码 JSON 失败，随后明确使用 UTF-8 重跑成功。此项不是产品缺陷。未运行无关全量测试，未新增依赖。

本批修改前后运行时 MD5 均为：mockup.js `CA9A7349D1CDBA88B33616DE9AE67740`；mockup.css `E2FC5FAEE0578F3BFADF0A6723F9C8C3`；live-data.js `019F54DFDE0629B0DDB67B91B46E50C7`；bridge.js `F297BF8769E249CA0D804F4F4EEEF5A9`。本批无产品改动，工作区混合修改保持。

## 启停与资源

参考启动：临时设置 `PYCHARM_PROPERTIES=D:/JetBrains/PyCharm-2026.2.1-reference/reference.properties` 后执行以下命令，并在 finally 恢复原环境值。

    Start-Process D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe -ArgumentList D:/JetBrains/PyCharm-2026.2.1-reference/fixture -PassThru
    taskkill /PID <本次启动的参考PID> /T /F

实际采集与自动清理：

    node tools/audit/capture-terminal-pair.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-15/terminal-live-rows/actual-clear --repeat-rows

脚本 finally 关闭 CDP、结束本次 Augit 进程树并删除专用 profile；若人工中断，只对本次日志记录的 PID 执行 `taskkill /PID <本次PID> /T /F`，不要按进程名结束其它实例。

参考 PID 20648 及记录的子进程 19572/15220/12944/3900/6932/20896/14012/4824/4464，实际 PID 23540/13136 均已停止；独立检查监听端口 58613/53277 均不存在，专用临时目录 Augit-terminal-pair-iIR0uS、Augit-terminal-pair-ZaRarM 均不存在，见 cleanup.json。本批未重试历史被审批拒绝的目录清理，不宣称历史全部资源已清理。

参考取证时一次输入因终端开关状态错误落入 notes.txt，已撤销并目视检查；最终逐字节确认文件仍为 UTF-8/LF 的三行 `Ag 0123456789`、`First line`、`Second line`，末尾带换行。

## 无遮挡文字区域补采

新增 actual-pointer-away/actual-light.png，SHA256 为 13de84efbb2bb48fcc113f86cbd0ea929b517341a3af28b435113e9c554f9d5e。通过 computer-use 点击只读编辑区空白，将系统鼠标及光晕移出终端，原图已目视核对。实际八行文字区域无遮挡，但焦点也移入编辑区，因此不冒充同焦点整页配对。严格采集前后前景、DPI、进程及原图哈希均核对通过。

full-eight-rows.json 记录阈值80下八行完整文字墨迹：两端每一行外接框均为178×21物理px，七个纵向步长仍相同。参考每行暗像素519–532，实际均562，尚不能归因为单一字形栅格原因：参考工具栏上的鼠标光晕可能影响邻近背景，完整RGB差异亦包含底色。不得据此增加文字粗细或固定平移。

第一次补采 actual-unobstructed 在45秒等待结束后退出，随后鼠标操作返回前景无进程；已重新枚举确认窗口消失，没有重用失效句柄。第二次在新实例观察后点击并采集成功。两份原始采集都保留，目录名不作为无遮挡证据。新增PID25084/2260、端口60882/52972和专用目录Augit-terminal-pair-4EHT6l/CJADNJ均由finally清理，再独立核验。未重新运行无关测试。
