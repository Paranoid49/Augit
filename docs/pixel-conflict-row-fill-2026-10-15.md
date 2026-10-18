# 冲突整行高亮覆盖整改

日期：2026-10-15。分支dsh，HEAD为3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。承接来源槽整改；整体目标仍未完成。Windows 10排除本次验证，跨屏DPI未验证。保留混合工作区，无回滚、暂存或提交。

## 依据与修改

参考仍为PyCharm 2026.2.1的原始conflict-line-pair/reference-initial.png，实际最终为conflict-row-fill-screen-final/actual-initial.png，二者均1730×1215，位于artifacts/pixel-followup-2026-10-15。design-system.md既定要求冲突底色覆盖正文整行；DiffDrawUtil使用HighlighterTargetArea.LINES_IN_RANGE。原实现背景只覆盖扣除正文padding后的行盒，中央和右栏末端出现白缝。

共享CSS通过等量负外距与内距扩展行盒，保留文字原点。正文采用单列minmax(max-content,1fr)网格，短行与最长行共享滚动内容宽度，避免横向滚动后短行右端露底。行高、词级Highlight、编号与动作层、处理边界SVG及保存文本未改写。左来源18px为参考未染色编辑器边距，保留；左来源右侧2px、中央/右来源两侧正文留白包含在行背景中。

## 失败迭代与证据

最初仅扩展行盒，在scrollLeft=80时短行右端仍缺80逻辑px；minmax(100%,max-content)轨道同样未随长行扩展。改为minmax(max-content,1fr)后通过。第一次真实原图采样发现左18px误染色，恢复该固定边距并补PNG断言，未把错误版本算作最终证据。

compare-conflict-row-fill.py在同一固定坐标(8,255)、(402,255)、(1128,255)、(1718,255)采样：参考依次为白色、#FFEEEB、#FFEEEB、#FFEEEB；修改前后两点为白色，最终四点与参考一致。结果保存在conflict-row-fill-screen-final/edge-comparison.json。没有缩放、平移或改写图片；仅证明这些采样点，不等于整行每像素或整窗通过。

三行左正文墨迹修改前后均为[35,204,171,222]、[35,244,171,261]、[34,281,157,299]，参考为[35,206,171,224]、[35,246,171,263]、[34,283,157,301]。横边界一致，垂直仍高2物理px，不加入猜测性的基线补偿。

## 分批验证

- 16组专项覆盖浅深、DPR1/1.75、正文13/40、横滚0/80；断言三栏文字原点、长短行共享范围、两边实际PNG颜色及左固定边距颜色。最终通过。
- 51项冲突模块回归通过，覆盖编辑、撤销、取消、失败恢复、外部变化与保存；10485078字节混合换行样本尾部编辑350ms、编辑及保存945ms，仅是本次样本。
- 72组布局与12组处理边界PNG栅格通过。原生单块完整保存通过；最终系统三状态身份与尺寸核验通过，初始实际图已目视。
- 最后的左边距修正只恢复该处既有留白，重跑16组专项及真实三状态，不重复51/72项。共享9资产、JS语法及git diff --check通过。未运行全产品套件或Shell构建。

CSS MD5从5B995560780CC614EEE26CF4AFD68193变为91CB995954680CA777EF336D27580103；mockup.js为69203C667E940D79782FA85B600D49B4，live-data.js为00E7ECB134CBD0939313286BE7C92E38，bridge.js为F297BF8769E249CA0D804F4F4EEEF5A9，后三者本批未修改。

## 启停与清理

本批实际PID15476/10224/24316，端口65150/51008/54863。驱动finally关闭本次实例并删除隔离目录Augit-merge-pair-MrWbDM/6JQqxh/QUvtJx；浏览器专项和模块测试finally关闭浏览器及服务器。不操作历史被拒绝的清理项。图片、JSON与审计脚本作为成果保留，没有新增临时诊断文件。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$chrome='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/audit/verify-conflict-row-fill.cjs $pw $chrome
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-row-manual $pw --light --reference-frame
# 正常由finally关闭；异常时只停止本次驱动输出的PID及其后代。
taskkill /PID <本次AUDIT_PID> /T /F
```

仍未满足55页及全部模态完整同态配对。终端逐行、背景刷新严格整表面与延迟、完整阴影证据保持未完成；本批只减少冲突行背景覆盖差异。
