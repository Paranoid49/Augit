# 正常刷新双时钟定位（2026-10-17）

承接连续写入正文露出反例，本批仅改审计工具，不增加产品等待或更改渲染路径。修复GDI编码时基并建立正常入口录像与桥接的双时钟关联。首批证据因重复时间戳被严格拒绝；修正后1289帧通过时钟校验，最差边缘变化位于撤盖应答后41.473毫秒。该组没有复现整块正文露出，不能用它给上一轮DDA正文反例定位或宣称修复。整体目标仍未完成。

## 工具改动与门槛

- `refresh-daily-file.cjs`增加显式`--gdi-clock`选项，默认DDA路径保留；正常启动、真实配置副本、默认Session、连续真实文件写入不变。桥接记录performance单调时钟和独立Date.now墙钟，不改变请求内容、接收者、应答或产品状态。
- `refresh-recorder-arguments.cjs`让GDI与DDA都使用1:1000000编码时基。原GDI默认按60Hz量化，首批1263帧有373个重复时间戳。新增参数回归修前失败、修后通过；不删除重复帧或降低严格门槛。
- `correlate-daily-refresh.py`核验采集路径、前景资格、录像SHA256、帧数、原始输入/滤镜/编码/封装时钟及桥接双时钟漂移；无应答、缺真实墙钟、非有限值、倒退或DDA相对时钟均不能冒充有效阶段证据。输出拒绝覆盖已有结果。
- 全表面分析仍包含585×252全部像素，不排除边缘或光标。前景监视只是离散身份采样，不能证明采样间绝无遮挡。

## 实机证据

目录均为`artifacts/pixel-followup-2026-10-17/`下：

1. `refresh-burst-gdi-clock`：入口成功、四轮连续修改完成，但完整录像分析拒绝，原因保存在clock-rejected.json。原始1263帧与日志保留，不计阶段关联通过。
2. `refresh-burst-gdi-clock-precise`：正常启动器强制非增量Debug构建，PID8000，端口49999；实际DPI168/DPR1.75、界面Microsoft YaHei UI12、正文13，实际正文节点确认随包JetBrains Mono。附CDP与GDI采样，不能外推无CDP或无测量扰动性能。

有效批次四次最后撤盖662.1/550.0/512.2/594.1毫秒，首轮观察695/568/530/602毫秒，均未满足500毫秒。每轮两批git/status串行，第二次写入在首批状态查询期间，最终正文为第二次版本。正常关闭跳转行后active=false、snapshotCount=0、captureCoverWindow=0、ownerEnabled=true。

有效录像1289帧、最大间隔40毫秒，SHA256=`d198b2e75f7f96988e4562350e33a558642ed0dacd34fbf03b2cfd2833931da2`。第75帧1.421秒为最差帧，265像素变化；已目视并独立分类为光标[218,107,221,133]内78像素，其余187像素范围[1,1,165,252]。完整比较排除像素仍为0，fullPresentationVerified=false。

daily-refresh-clock.json记录该帧输入墙钟1792221654946.473毫秒，距最近modal-uncover请求46.473毫秒、应答41.473毫秒，与模态桥接请求没有重叠。此证据只定位本次边缘变化；不证明根因，不把它套用于上一轮正文露出，也不把离散采样等同所有桌面Present。既有GDI历史同类边缘结果继续有效，本批补齐的是最新正常启动器、连续真实修改与实际执行身份下的关联。

实际映射Shell SHA256=`94EF57F58A44D101CAECF8E3A8E9AD79B10E0C758E62BCCD42C8105ACF78EF46`；执行live-data.js SHA256=`47BA918518C9185AEB2F31E3FED50454879E89228B315B2E702D61FD1ACC1D6D`，均与当前产物/源码一致。正文实际字体、配置及缩放见runtime.json；mapped-images.json仍确认AudioDevProps2.dll注入，不能断言其因果或计干净环境性能通过。Release未更新。

## 验证与资源清理

审计场景和录屏参数14项JS测试通过；新增时钟适配5项及既有时钟8项Python测试通过。仅定向验证，未运行全量测试。JavaScript语法与git diff --check通过。

两批cleanup-independent.json分别确认：4828/9532/17972、8000/6416/21512已退出，50588/49999无监听，系统Temp下Augit-daily-refresh-wSI3Dc和Augit-daily-refresh-BRud3T已删除；分析和解码进程已结束。历史参考清理自动审批拒绝及Nahimic服务访问权限阻塞未解除，本条不是全局零残留声明。

设置秒级慢开、无CDP完整开关、透明帧和500毫秒仍未完成；55页/全模态、终端完整配对及阴影仍未验收，复杂Git及冲突精修沿用户最新方向暂缓。下一步应取得正文露出与阶段同钟证据，或继续设置独立窗口根因；不要重复已撤销的遮盖自身捕获候选。

## 启停与复验

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 优先正常关窗；异常时仅结束本次已核实的实例。
taskkill /PID <本次PID> /T /F
node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新证据目录> --git-fixture --burst --gdi-clock
D:/Python/Python3119/python.exe -B tools/audit/analyze-static-modal-video.py artifacts/pixel-followup-2026-10-17/<新证据目录>
D:/Python/Python3119/python.exe -B tools/audit/correlate-daily-refresh.py artifacts/pixel-followup-2026-10-17/<新证据目录>
```
