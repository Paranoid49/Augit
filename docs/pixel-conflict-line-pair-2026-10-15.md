# 冲突三状态逐行证据与粗点线整改

日期：2026-10-15。分支 dsh，HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。保留既有混合修改，未暂存、提交或回滚。Windows 10 按用户要求排除本次验证；跨屏 DPI 保留未验证。

## 原始证据

证据目录：artifacts/pixel-followup-2026-10-15/conflict-line-pair。reference-initial/left/resolved.png 是 PyCharm 2026.2.1 的系统原图及对应身份 JSON，DPI 168、1730×1215。actual-states-retry 是修前实际三状态；actual-quantized 是最终实际三状态，1594×848。两端窗口尺寸和结构不同，不能通过整窗同态配对门禁，不能增加 55 页合格配对数量。

同一 UTF-8/LF 夹具：first line、base/ours/theirs value、last line，末尾 LF。初始中央 base，接受左侧后中央 ours，忽略右侧后全部解决。参考端从未 Apply。

实际核验的 base/result SHA256：14F6DB9E5F72A87AFEB79DFF24E93B5C9E4C457CFA060134CB7C58B977B51CF5。实际端三状态驱动断言草稿、动作清除和磁盘未保存。参考 resolved 中央第二行被完成提示遮挡，不计为无遮挡文字像素证据。

初始左栏 first line 墨迹参考 135×18、实际 136×18；ours value 134×17 对 135×17；last line 两端 122×18。首行至第三行墨迹顶边均相隔 77 物理像素。字串宽差、抗锯齿和全行基线仍未完全相同。

## 修改及限制

来源：本地 IntelliJ UIUtil.drawBoringDottedLine 每 4 逻辑像素绘制两行短线，DiffDrawUtil.drawChunkBorderLine 在 y-1 绘制；空范围在 y-1 和 y 重复。原参考图左栏已处理上边界 y233–236、下边界 y270–272，着色 4、留白 3 物理像素，周期 7。

共享 CSS 的正文/编号槽已处理边界从 1px dotted 改为矩形重复渐变，宽高按既有 DPR 令牌取整；空范围合并为 3px 高。绝对定位保持真实行盒、选区和保存文本。连接带使用既有路径，本批未改变其曲线。

初版渐变实机只着色 3、留白 4，已加设备像素量化。最终 actual-quantized 左栏主体着色 4、留白 3，水平周期与参考一致；上边界纯色主体只有两排，另有抗锯齿边缘，下边界也有不同栅格，尚未匹配参考上下厚度及相位。专项测试只验证周期、主体宽度、可见厚度下限、行盒与文本，不冒充全部像素验收。整窗结构仍不同，按 AGENTS 规范冲突规则已请求用户决定是否同步修改布局规范。

## 验证与失败记录

- 新增 conflict-boundary-raster.cjs / read-conflict-boundary-raster.py：深浅 × 100/175/200% 六组 PNG 点阵检查通过，无落盘临时图。测试夹具初次相对定位、主题属性位置及跨语言转义错误已修正；亚像素边缘与纯色主体分开度量，未隐瞒边缘差异。
- capture-conflict-reference-shell.cjs 新增 --screen-pair：初始、接受左侧、全部处理系统图，前景/PID严格门禁，草稿与磁盘断言。actual-states 因前景身份不符失败并清理，actual-states-retry/actual-quantized 成功。
- 最终实际浅色短流程、浅色空侧删除精确保存、深色多块编辑/撤销/保存通过。没有运行全量测试。
- 9 共享资产一致，两个改动 JS 语法与 git diff --check 通过。
- CSS MD5：09D3687D25A62F2377D117F3AE78599F → 4325EF540DE29936758200BB855E68BF。mockup.js / live-data.js / bridge.js 分别仍为 3B3399F68FD796B48952022625C3B5F4、B48E37B755997A2C28D98AF8CB87E7ED、F297BF8769E249CA0D804F4F4EEEF5A9。

## 启停及资源

参考 PID6768 已 taskkill /T /F。实际初轮 PID15980/8200/4344 和本批 PID23364/6708/6552/20216/22004/10384 均由 finally 退出；隔离临时目录按驱动输出回收。没有常驻服务器。正式 PNG、身份 JSON 和夹具保留为证据；历史被拒绝清理的目录没有重试。

启动及必要时手动停止（PID 只取本次驱动输出，不停止同名用户进程）：



```powershell
node tools/audit/capture-conflict-reference-shell.cjs artifacts/pixel-followup-2026-10-15/conflict-line-pair/manual C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright --light --screen-pair
taskkill /PID <AUDIT_PID> /T /F
```

定向栅格测试：

```powershell
node tools/audit/conflict-boundary-raster.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe
```

## 直接矢量边界接续

本批先在隔离浏览器复现渐变上下边缘丢失：替换遮罩、SVG背景和像素化采样均未改善；SVG pattern 在真实WebView2仍把4px着色压为3px。最终改为独立只读SVG路径，每个可见点直接绘制设备像素矩形，不使用纹理图案。覆盖层是编辑器的兄弟节点，pointer-events:none、aria-hidden，不进入文本、选区或保存。滚动只更新已有路径，内容变化重新建立范围；仅生成可见宽度的点，长行不放大路径数据。

actual-vector/actual-resolved.png 原始系统图：左栏上边界 y264–267 共4排，下边界 y301–303 共3排，每排着色4/留白3。参考上边界 y233–236、下边界 y270–272 相同。boundary-local-comparison.json 对参考两个200px宽区域进行纯平移局部配准（dx106、dy31），两个区域共3000像素均零差异；这是去除整窗几何影响后的局部图案证据，不代表窗口定位、三栏宽度、文本或全窗口同态通过。参考范围[50,231,250,239]及[50,268,250,275]；两端均无缩放、滤镜或图像改写。

最终12组（双主题、100/175/200%、整数/分数横坐标）PNG测试逐排断言所有纯色像素、周期、点宽和完整厚度，另验证滚动复用、行盒与正文不变。实际深色多块vector-multi、浅色空侧vector-empty和完整单块vector-single-complete通过，包含精确保存、编辑、撤销、重做和外部刷新。共享9资产、JS语法、git diff --check通过；未运行全量。

客户区标记首次采集失败（actual-svg、actual-svg-geometry、vector-single-retry）；保留失败证据并清理。系统配对使用独立系统PNG身份门禁，取消不必要的CDP前置截图；普通CDP路径增加标记插入后背景队列结束等待。完整单块旧选择器同时匹配正文与编号背景，已限制到conflict-line，失败vector-single/vector-single-final不计通过。

mockup.js MD5：3B3399F68FD796B48952022625C3B5F4 → 6F4A29DE66FF139F95CBDEF6074B5CBD；mockup.css：4325EF540DE29936758200BB855E68BF → 411B555CAC9293854DD200E3C1C08625。live-data/bridge不变。产品修改仅两份共享JS/CSS。

本批实例PID5680/23444/15516/12980/21500/4952/23160/11020/5132/18288/18832均由finally停止并清理隔离profile；端口52193/52435/57669/64516/51207/58273/54391/61237/59837/63351/59373。实验浏览器均finally关闭。实验驱动移至tools/audit/conflict-boundary-probe.cjs作为可复验审计工具，PNG是保留证据。启停方式与上节相同，--multi执行多块、--empty --light执行空侧、--light执行完整单块，正常自动清理，异常只针对输出PID执行taskkill /PID <AUDIT_PID> /T /F。

整体仍未完成。冲突整窗重排等待规范裁决，正文/连接带/其他字号参考配对尚未完成；背景刷新、终端和系统阴影继续沿各自未完成记录推进。
