# 跳转行完整外沿与内侧圆角复验（2026-10-16）

上一轮有实际产品修复和回归，属于有效进展。本轮补齐此前鼠标光晕污染和内侧角区缺口；不修改产品阴影、字号或布局，不重跑产品全量测试。整体整改目标仍保持未完成。

## 当前证据

两端均为Windows11、175%缩放、活动态、浅色跳转行窗口，DWM物理矩形584×252。参考为PyCharm2026.2.1，原始资料位于artifacts/pixel-followup-2026-10-15/shadow-goto-reference-light-192；新Augit资料位于artifacts/pixel-followup-2026-10-16/shadow-goto-pointer-clear。当前Augit源网页与已构建Debug产物采集，前后背景图完全相同，未缩放、补色或筛除差异像素。窗口几何仍按参考控制，仅用于阴影同态比较，不充当默认几何验收。

| 检查 | 结果 |
| --- | --- |
| 192物理像素完整外环，八区共468480像素 | 全部背景一致，最大RGB差0，超容差像素0 |
| 最外层3204像素 | 两端均精确回到原背景，未截断阴影 |
| 四个16×16内侧角区，共1024像素 | 全部背景一致，RGB逐像素差0 |
| 指针距离 | 采集前后均在[707,115]；完整采样区域[1061,693,968,636]之外至少128物理像素 |
| 背景重复采集 | background与background-repeat的SHA256完全相同 |

原始两端settings.png均已目视查看，本次ROI没有鼠标光晕、其他窗口或通知遮挡。原始元数据unoccluded仍保持采集器的null，不把人工观察改写成自动检测结果。当前前图SHA256为1457c5f9dc9cf73a97e38d24d6d80523612b87edb9666743101a3cbb04c6921d；参考前图为4f0456dbcf9a7af2a66de282d4f1a638b53179a01a717ab90aea04a03b0b839d；两端背景均为9431131faa6735ec20457b91a6d7a8e9b86265af9a05b9a8eab4d162d4f62827。

ring-complete-gate.json保存完整外沿与采样边界联合门禁；inner-corners.json保存内角比较。两个局部结果共同补齐本次浅色活动样本，不能代表全部系统阴影、整窗正文或所有模态。深色、非活动态、独立Settings新路径及其他窗口仍未完整配对；既有深色Settings局部证据来自旧架构，不能移用为新实现验收。Win10按用户要求排除，跨屏DPI保留未验证。

## 历史反证与工具整改

新的内角比较器同时复验2026-10-15的shadow-goto-actual-light-192：每角29个不同像素，其中21个超过3级RGB容差，最大22。命令按预期退出1，结果保存在本次目录historical-inner-corners.json。当前对应值均为0，证明既有原生圆角去重绘修复得到实机验证。旧污染图shadow-goto-current-complete也补了inner-corners.json，仅四角通过；该图外环仍被光晕污染，不能用此局部结果替代新图。

修改文件：

- capture-shadow-shell.cjs：跳转行入口用正文focus及Ctrl+G，关闭用Esc，保留原生检查点的指针位置；采集前景和背景都强制PointerClearance=128。
- compare-inner-corners.py及test_inner_corners.py：四角完整原始RGB比较、同背景校验、同DPI同活动态同窗口尺寸、来源及SHA校验，禁止覆盖证据。
- compare-shadow-ring.py及test_shadow_ring.py：新增completeExternalShadowPassed，命令退出成功同时要求像素一致及外沿回到背景。之前两端同样被截断时allExternalPixelsPassed虽为true，不能让命令据此返回成功。

定向测试：内角5项通过（0.050秒），外环7项通过（0.160秒）；JS语法、9共享资源字节一致、git diff --check通过。未改产品或运行时资源，未新增依赖。

## 复验与清理

```powershell
$pw = 'C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
# 使用新目录；检查点处通过原生界面将指针留在主窗标题空白区，再创建main-active-ready。
node tools/audit/capture-shadow-shell.cjs $pw <新证据目录> Light --normal-entry --uniform-background --goto-line --place-inside-body --shadow-radius 192 --reference-frame artifacts/pixel-followup-2026-10-15/shadow-goto-reference-light-192/settings.png.json --activate-main
D:/Python/Python3119/python.exe -B tools/audit/test_inner_corners.py
D:/Python/Python3119/python.exe -B tools/audit/test_shadow_ring.py
# 正常启动与停止；异常停止只使用启动器打印的本次PID。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
taskkill /PID <本次PID> /T /F
```

本次采集PID24744、端口53644、临时目录Augit-shadow-pair-pdzujw已由finally清理，独立复核均不存在；main-active-ready由检查点finally删除，无本轮进程、端口或临时目录残留。原始PNG和JSON作为证据保留。此前被审批拒绝的清理目标未重试。

分支dsh、基线提交3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；进入本批155个已跟踪文件混合修改、12973插入及5209删除，未回滚、暂存或提交。本批前后四网页MD5不变：mockup.js=5148D9CDA4D61A8F11D0F125407918B8，mockup.css=D5342D22C245695A57F93BC90A1DC7ED，live-data.js=3216B5CDC0FE86166B87389B7D6B3954，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。
