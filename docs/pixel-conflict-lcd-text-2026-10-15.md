# 冲突正文LCD栅格整改

日期2026-10-15，分支dsh，HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。整体目标未完成；Windows 10排除本次验收，跨屏DPI未验证。混合工作区未回滚、暂存或提交。

## 证据链

前批CSS试验未得到有效改善。本批读取Chromium官方picture_layer_impl.cc：LCD文字会因contents-not-opaque、will-change-transform等原因被拒绝；main分支源码只解释机制，不作为当前Edge版本完全等价的证明。来源URL及SHA256记录于conflict-lcd-final/chromium-source-provenance.json。

新增trace-lcd-text.cjs，通过隔离实例CDP追踪官方cc.debug类别。首轮因getCategories返回空列表而拒绝，驱动已清理；修正为请求官方类别并检查实际事件。conflict-lcd-runtime/lcd-trace.json包含1097事件、17个LCD状态项：根层允许，layer_id=20的LayoutBlockFlow DIV class='scrim'明确contents_opaque=false、lcd_text_disallowed_reason=contents-not-opaque，Overlap合成；不是仅凭截图推断。

真实对照结果（首行白底范围[20,200,190,226]，最小RGB通道小于230）：

| 条件 | 墨迹像素 | 非灰阶像素 | 决定 |
| --- | --- | --- | --- |
| 原透明层 | 686 | 0 | 发现缺陷 |
| scrim改不透明白色 | 784 | 576 | 证明透明合并层影响，但会破坏背景，不采用 |
| 对话框translateZ(0)，保留或去掉圆角 | 686 | 0 | 无效，不采用 |
| 正文translateZ(0)，不设置will-change | 784 | 576 | 限定正文修正 |
| PyCharm参考 | 781 | 606 | 仍非全部像素相同 |

前批opaque-text-layer试验同时设置了will-change，其本身就是LCD拒绝条件，不能把那个失败外推到全部独立正文层方案。本批反证已区分。试验目录分别为conflict-lcd-opaque-scrim、conflict-lcd-dialog-layer、conflict-lcd-text-layer；每组恢复后首行回到686/0。

## 产品修改

仅在共享mockup.css的.conflict-block增加translateZ(0)，使有不透明底色的正文单独栅格化，不加will-change，不平移文字，不改变透明遮挡层和背景快照。同步两份共享样式及规范。没有新增依赖、改变字体、系统设置、技术栈、行高、基线或保存契约。

最终conflict-lcd-final三状态1730×1215原始系统PNG与参考完整不同像素83025/89589/99174，正文27167/27845/31044，标题分隔线均0。相较前批正文减少129/75/130像素；完整与局部仍有大量差异，不能由总量下降宣称全窗或字体复原。左首行仍[33,205,169,223]对参考[35,206,171,224]，坐标偏差未修复。最终首行784墨迹/576非灰阶，lcd-channel-comparison.json保留哈希和范围。

## 验证

- 24组整行PNG/横滚通过，新增独立层矩阵必须为单位矩阵、will-change必须auto的断言。
- 60组双主题/多DPR/字号及焦点分隔线栅格通过。
- 真实深色多块conflict-lcd-save：行号横向固定、垂直difference=0、视口外焦点可见、exactSaved=true。
- 两项追踪流测试覆盖空类别发现及读取错误清理；两项探针流程覆盖10种模式正常恢复和注入截图失败恢复。模式集合扩展不等于十模式整组已做一次系统截图。
- 未跑全量套件或C#构建。额外合成层内存成本未量化，不宣称性能无影响；背景刷新、终端、全55页/模态和完整阴影仍待完成。

## 哈希与清理

mockup.css MD5由5381EE70C8FCED1C11969B71C6B5774E变为C2A61BEC007B0CFE47D1698E591ED3AD；两份共享相同。mockup.js 56AF172CFF9BBD45FDFDFE5F9003143B、live-data.js 00E7ECB134CBD0939313286BE7C92E38、bridge.js F297BF8769E249CA0D804F4F4EEEF5A9未变。

本批Augit PID6228/14132/24704/24112/5864/14952/11520均退出；端口59429/50079/49753/53777/59356/62026/53498无监听；临时目录Augit-merge-pair-0Z3cJR/P7noNU/T8tSkx/QZ2ySS/9PNhEL/KTYp6b/66L4qo均删除并只读核验。追踪流、CDP、测试浏览器及测试临时目录均清理。未启动PyCharm，不重试历史被拒绝清理。

原始截图、追踪及JSON为正式证据保留。未扩大通过范围、放宽容差或配准原图。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$chrome='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/audit/verify-conflict-row-fill.cjs $pw $chrome
node tools/audit/verify-conflict-title-separator.cjs $pw $chrome
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-lcd-manual-new $pw --light --screen-pair --reference-frame --pair-focus --lcd-trace
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-lcd-save-manual-new $pw --multi --reference-frame
node --test tools/audit/trace-lcd-text.test.cjs
node tools/audit/test-text-composition.cjs $pw $chrome
# 正常由finally停止；异常只停止本次打印的PID。
taskkill /PID <本次Augit_PID> /T /F
```
