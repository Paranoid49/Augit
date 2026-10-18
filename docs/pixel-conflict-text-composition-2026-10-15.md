# 冲突正文灰阶合成排查

日期2026-10-15，分支dsh，HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。整体整改未完成；Windows 10排除本次验收，跨屏DPI未验证。保留混合工作区，未暂存或提交。

## 现象与依据

上一批白底首行[20,200,190,226]：参考781墨迹像素中606个RGB通道不等，实际686墨迹像素全部灰阶。当前控制器DefaultBackgroundColor为不透明主题色；系统字体平滑类型读取为2，未改系统设置。历史焦点切换截图仍为灰阶，不能将问题单独归因于光标。

新增隔离探针probe-text-composition.cjs，真实Git冲突三状态驱动通过--text-composition-probe调用。探针记录Browser.getVersion、SystemInfo.getInfo、DOM计算样式、LayerTree及合成原因；临时样式每次恢复，finally再次恢复并断开调试会话，不写磁盘草稿或产品样式。

运行时为Edg/155.0.4283.45，ANGLE D3D11、AMD Radeon 780M；GPU合成与栅格化开启，auxAttributes.subpixelFontRendering=true。这只证明运行时能力标志，不证明此处文字实际启用了子像素渲染。没有额外字体绘制启动开关。生产技术栈、字体资源及用户设置均未更改。

## 两批真实对照

证据位于artifacts/pixel-followup-2026-10-15/conflict-text-composition与conflict-text-layers，每批保留原始系统PNG、身份JSON、环境/合成层及text-channel-analysis.json。

| 条件 | 首行墨迹数 | 非灰阶像素 | 相对同批基线首行不同像素 |
| --- | --- | --- | --- |
| 去掉圆角及裁剪 | 686 | 0 | 0 |
| html/body/dialog不透明白底并取消圆角裁剪 | 686 | 0 | 0 |
| 隐藏透明scrim | 686 | 0 | 0 |
| 每栏正文独立3D合成层 | 686 | 0 | 637 |

独立层的LayerTree明确出现Trivial3DTransform及WillChangeTransform，证明条件生效；灰阶问题没有改善，而且正文像素改变，不能作为修复。第二批restored与baseline整图零差异；第一批恢复后首行零差异，但整图641像素不同，不能声称该批整图恢复完全一致。原始证据不平移、不重采样、不剔除失败。

四种试验均不保留到产品。最终探针合并六种模式（基线、四个条件、恢复）便于复验；原始两批各四张，不冒充合并后六模式的一次实机执行。没有宣称根因已确定、文字像素通过或增加55页合格数。下一步应检查设备缩放与文字栅格策略的运行时条件，避免重复圆角、透明根、scrim和独立层试验。

## 验证和清理

test-text-composition.cjs包含两个真实隔离浏览器流程：六模式条件及草稿保持、注入截图失败后的样式恢复。测试最初发现读取style后缺失属性可能序列化为空串；已只将空串/缺失视为无声明，其余内容仍严格比较。最终两流程通过，不冒充系统像素通过。未运行产品全量、构建或无关模块回归。

Augit PID11684/16688已退出；端口59868/51817无监听；临时目录Augit-merge-pair-GZuACT/azMveu已删除并只读核验。测试浏览器及Augit-text-probe-test临时目录由finally回收。未启动PyCharm，不重试历史被拒绝清理。正式证据保留。

产品MD5未变：mockup.css 5381EE70C8FCED1C11969B71C6B5774E，mockup.js 56AF172CFF9BBD45FDFDFE5F9003143B，live-data.js 00E7ECB134CBD0939313286BE7C92E38，bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$chrome='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/audit/test-text-composition.cjs $pw $chrome
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-text-composition-manual-new $pw --light --screen-pair --reference-frame --pair-focus --text-composition-probe
# 正常由finally停止；异常仅停止本次驱动输出的PID。
taskkill /PID <本次Augit_PID> /T /F
```
