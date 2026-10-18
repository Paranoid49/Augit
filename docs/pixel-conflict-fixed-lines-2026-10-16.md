# 冲突固定逐行区域诊断

日期：2026-10-16。沿用 dsh、HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6 的混合工作区；未暂存、提交或回滚。此批只修改审计工具与记录，没有修改产品布局或启动方式。

## 依据与结果

重新核对本地 ThreeDiffSplitter.java 与既有 reference-runtime-geometry.json：988逻辑像素的三栏为313／314／313，两条连接带各24；当前计算符合这一规则。中央槽线43、右来源槽线76及正文前3逻辑像素留白也符合既有规则。不能仅凭约2物理像素残差再加入固定平移。

新增 compare-conflict-lines.py，对既有原始系统图的初始、接受左侧、全部处理三个状态，在每栏的三个固定行区域逐一记录原始RGB差异及墨迹边界。两端使用相同区域，不移动、重采样或重写PNG；输出采用独占创建，拒绝覆盖已有结果。

证据：artifacts/pixel-followup-2026-10-16/conflict-gutter-inside-final/line-diagnostics.json。参考来自 artifacts/pixel-followup-2026-10-15/conflict-reference-clean。初始原图SHA256参考为6d07a0d20d6e944c8f0c1e6c680913b609e2d0086723eb67829d26328bd9cc68，实际为3d9e95df84630d0df90e02f22ca3926b63e871c4e14c85fc1861541829b495db。全部六张图的身份和哈希均登记在结果中。

- 共27个固定区域，原始像素完全一致的区域为0；单区域不同像素998至2448。固定区域包含文字、背景和状态装饰，不能把数量都归为字体差异。
- 初始状态三栏首行及第三行墨迹边界均相对参考左移2物理像素，纵向边界一致；第二行左边界差-1、右边界差-2，纵向边界一致。阈值为三通道均不超过140，不能扩张成全部抗锯齿像素一致。
- 接受左侧及全部处理的中央首行墨迹边界包含光标影响，差值为[-3,-5,-2,7]；不能把该区域当成纯文字的基线证据。后续需要统一光标状态再采集。
- 参考截图裁框原点为[575,245]，实际为[361,261]。参考运行时几何来自另一诊断进程，因此本批不把逻辑坐标乘DPI的推算作为精确设备相位证明；下一步需同一采集实例取得客户区原点与光标状态。

本批未确认残差的产品根因，也未宣称修复冲突整窗。设置双窗口及闪烁的最新实现和限制仍见 settings-independent-webview-2026-10-16.md；本批没有追加其通过状态。55页与全部模态目标仍未完成。Windows10按用户决定排除，跨屏DPI继续未验证。

## 验证与资源

4项反例测试通过，覆盖一像素位移不得配准、缺失文字、相邻行边界隔离、错误哈希／缩放／重采样拒绝。测试耗时0.152秒，9项共享资产一致；git diff --check通过。没有启动应用、浏览器或服务器；测试临时目录由TemporaryDirectory回收，收尾未发现Augit-line-check-*目录。原有被策略拒绝的目录及标记没有重试删除。PNG与JSON作为证据保留。

产品四文件MD5前后不变：mockup.js=5148D9CDA4D61A8F11D0F125407918B8，mockup.css=D5342D22C245695A57F93BC90A1DC7ED，live-data.js=3216B5CDC0FE86166B87389B7D6B3954，bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。没有重跑产品全量测试或构建。

```powershell
# 本批纯离线诊断，不启动服务；输出文件必须尚不存在。
D:/Python/Python3119/python.exe -B -m unittest discover -s tools/audit -p test_conflict_lines.py
D:/Python/Python3119/python.exe -B tools/audit/compare-conflict-lines.py artifacts/pixel-followup-2026-10-15/conflict-reference-clean artifacts/pixel-followup-2026-10-16/conflict-gutter-inside-final artifacts/conflict-lines-manual-new.json
# 日常启动；停止优先正常关闭，异常时只停止启动器打印的本次PID。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
taskkill /PID <本次PID> /T /F
```
