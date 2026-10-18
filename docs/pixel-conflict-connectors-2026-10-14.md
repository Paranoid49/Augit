# 三栏冲突连接带整改（2026-10-14）

## 来源与实施

本批继续[冲突行号槽](pixel-conflict-gutter-2026-10-14.md)。既有原始参考PNG中两条连接带分别约为42物理px，在175%下为24逻辑px。权威`platform/util/resources/misc/registry.properties:1073`的`diff.divider.width=24`，`ThreeDiffSplitter.java:107–118`先扣除两条连接区再等分正文。`MergeThreesideViewer.java:1176–1194`按每侧范围和处理状态绘制；`DiffDividerDrawUtil`的ResolvablePainter未处理填色、已处理仅点线；`DiffDrawUtil.java:76,151–225`使用0.3/0.7三次贝塞尔控制点。

共享CSS增加24px列间距，共享JS在正文外创建两个只读SVG显示层。按稳定块ID连接来源行范围与当前草稿字符范围，处理状态独立跟随左右侧；接受后对应侧移除填充并显示上下点线，撤销恢复。空范围绘制双边界，不加入可编辑占位文本。草稿末尾无换行时，结束范围按最后实际字符所属行计算，不能把非空末行误判为空。

内容或度量变化时缓存行范围的内容坐标，滚动时复用路径节点并更新曲线；SVG裁切到正文可见区域，不接收指针事件。静态视觉稿只连接显式标注、数量一致的连续块；真实数据没有可靠草稿时不猜测对应关系。原三栏只读/编辑边界、保存和滚动机制保持。未增加依赖或后台任务。

## 验证与失败记录

证据目录均位于`artifacts/pixel-followup-2026-10-14/`。

| 目录 | 实际结果 |
| --- | --- |
| merge-connectors-first | 首次多块回归通过，初始PNG目视曲线可见；后续已按权威调整控制点 |
| merge-connectors-multi | 多块初始/滚动/双方处理，24px宽度、来源端点误差0、状态与填充、节点复用、精确保存通过 |
| merge-connectors-eof、merge-connectors-eof-aligned | 取证标记越界失败，不计截图通过；当时标记坐标在异步重排前读取 |
| merge-connectors-eof-current | 取证时重新读取标记位置后通过，保留严格三色像素检查；无末尾换行的草稿范围及精确保存通过 |
| merge-connectors-light | 浅色初始/单侧处理、追加/撤销通过，单侧处理图已目视：左点线、右填充 |
| merge-connectors-empty-tail | 旧测试把正文与行号槽两份空侧显示层都计入，2不等于1而失败 |
| merge-connectors-empty-tail-final | 限定正文选择器后通过，未改产品来凑计数 |
| merge-connectors-empty-current | 最终空范围双边界实现：初始/处理后端点、状态、无占位保存通过 |
| merge-connectors-layout、merge-connectors-layout-current | 旧静态测试仍使用随包字体前的行高及旧三栏配色，失败保留 |
| merge-connectors-layout-final | 按既有独立JBR13/40行盒22/64与已登记冲突色更新期望；72组主题/DPI/独立字号/按钮/首行空侧通过，新增连接带宽度和编辑边界断言 |

共享9项资产、三个修改JS语法及git diff --check通过；仅跑受影响三栏场景，未运行仓库全量或未修改的Shell测试。真实多块证据先于最终静态回退分支及空范围双线微调；最终空侧专项和72组静态覆盖后两项，未把早期运行写成最终全场景复跑。

此批确认连接结构和处理/滚动行为，未新增PyCharm全状态无损配对。精确点阵、抗锯齿、高斜率细线、双方不同可见高度、超大文件性能及完整三栏外壳仍待验；来源端点断言不等于中央所有编辑偏移均完成参考核对。55页及全模态、终端、刷新延迟与完整显示帧、阴影四角/非活动证据仍未完成。Windows10排除本次验收，跨屏DPI保留未验证。

## 复验与清理

```powershell
$pixelPlaywright='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$pixelChromium='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/audit/capture-conflict-reference-shell.cjs artifacts/merge-connectors-multi-new $pixelPlaywright --multi
node tools/audit/capture-conflict-reference-shell.cjs artifacts/merge-connectors-light-new $pixelPlaywright --capture-only --light
node tools/audit/capture-conflict-reference-shell.cjs artifacts/merge-connectors-empty-new $pixelPlaywright --empty --end-delete
node tools/audit/capture-conflict-reference-shell.cjs artifacts/merge-connectors-eof-new $pixelPlaywright --empty --eof
node tools/verify-ux-conflict.cjs $pixelPlaywright $pixelChromium artifacts/merge-connectors-layout-new
powershell -NoProfile -File tools/audit/verify-ui-assets.ps1
node --check web/src/mockup.js
node --check tools/audit/capture-conflict-reference-shell.cjs
node --check tools/verify-ux-conflict.cjs
git diff --check
# 正常finally自动停止实例、浏览器并删除临时仓库/profile；异常只停止本次打印PID。
taskkill /PID <本次Augit_PID> /T /F
```

独立查询本批Augit PID18908/8784/7492/9632/6180/18032/4000/11652/17488全部退出；端口59273/62168/58925/49704/63298/53782/50210/54188/55075均无监听。临时目录Augit-merge-pair后缀OynFK1/KKVH8H/vsZ6hv/qS4aCN/leb4Oa/NwB3oN/ARJFIE/cUxfb4/Xj1nak均不存在；最终布局测试会话已退出，无该命令行进程残留。未启动新的PyCharm参考或服务器；历史被拒绝清理目录未重试。原始证据保留。

修改共享mockup.js/CSS双份、真实取证驱动和静态三栏测试。JS MD5由`3BE58955301492EC6DA96F177A45BA54`至`DB12568D024253582D1C09B5DA1CD5AE`；CSS由`C124EBD0CDC60E7EC24F7485F948A399`至`439C96E4A5696967DC702CAC9B9A0850`；live-data/bridge仍为`DB52470B2BAB434DE4E463086A39FA6D`/`F297BF8769E249CA0D804F4F4EEEF5A9`。混合工作区未回滚、暂存或提交。
