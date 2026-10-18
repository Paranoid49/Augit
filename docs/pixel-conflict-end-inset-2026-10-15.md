# 冲突正文尾部留白整改

日期2026-10-15，分支dsh，HEAD 3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。延续用户已批准的PyCharm实机重排并同步规范。整体目标未完成；Windows 10排除本次验收，跨屏DPI未验证。未暂存、提交或回滚混合工作区。

## 依据与修改

现有reference-runtime-geometry.json记录参考262.9437.214三个EditorComponent：左来源Insets.left=18/right=0，中央及右来源left=0/right=18。当前CSS中央与右来源尾部仍为12px；这影响长行横滚到底后的末尾留白，并非文字原点偏移。

将共享mockup.css的默认padding-right和整行背景扩展同步改为18px，左来源镜像规则保持左18px、右行号槽及3px正文间隙。docs/ux-spec.md与docs/design-system.md同步。未调整行高、基线、文字平移、分栏或原生窗口。

## 验证

- verify-conflict-row-fill.cjs由16组扩展24组：双主题、DPR1/1.75、代码字号13/40、横滚0/80/最右端。除整行背景PNG采样、原点保持外，增加计算内距及最后字符到可见边缘的距离断言；末尾中央/右18px，左来源到槽3px，允许不足1px的滚动取整。
- 真实深色多块场景conflict-end-inset-save通过：行号横向固定、垂直对齐difference=0、视口外焦点可见、exactSaved=true。
- 最终浅色三状态系统图conflict-end-inset-frame采集成功，1730×1215、DPI168、统一焦点。对参考完整差异仍83154/89632/99240，正文27296/27920/31174，标题横线0；与前批conflict-client-frame计数一致。短文本无需尾部空间，不能把本次整改宣称为文字偏移修复。
- 原点核对发现参考根宽988逻辑px，而当前按原始框1730/1.75得到988.571px；两端窗口客户区及设备坐标取整仍需厘清。不用固定1/2物理px平移掩盖差异，不增加55页完整同态合格数。
- 未跑全量或无关布局套件，无C#或公共协议变化；仅执行受影响像素/横滚专项和真实保存、最终截图。

## 哈希与资源

mockup.css MD5从F9F29FAB9339BD2420A216408F2E00D7变为5381EE70C8FCED1C11969B71C6B5774E，两份共享文件相同。mockup.js 56AF172CFF9BBD45FDFDFE5F9003143B，live-data.js 00E7ECB134CBD0939313286BE7C92E38，bridge.js F297BF8769E249CA0D804F4F4EEEF5A9，均未变。

本批Augit PID23384/7380、端口56605/59480、临时目录Augit-merge-pair-XzNDsu/IgV9Ac由finally回收。浏览器专项与只读尾部探针均finally关闭。未启动PyCharm或常驻服务器，正式截图和JSON保留；历史被拒绝清理目录不重试。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$chrome='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/audit/verify-conflict-row-fill.cjs $pw $chrome
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-end-inset-save-manual-new $pw --multi --reference-frame
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-end-inset-frame-manual-new $pw --light --screen-pair --reference-frame --pair-focus
python -B tools/audit/compare-clean-conflict.py artifacts/pixel-followup-2026-10-15/conflict-reference-clean artifacts/conflict-end-inset-frame-manual-new
# 正常由finally停止；异常仅使用本次输出的PID。
taskkill /PID <本次Augit_PID> /T /F
```

## 文字边缘后续定位

对原始初始图白底首行[20,200,190,226]取最小通道小于230的墨迹：参考781像素中606个RGB通道不相等；实际686像素全部为灰阶。text-edge-channels.json保存范围、阈值与原图哈希。这证明颜色边缘表现不同，不能推断具体渲染API或用位置补偿解决全部差异。后续需检查透明表面与文字合成路径；未修改字体抗锯齿或擅自更改参考设置。最终三张实际PNG与前批对应原图数组全部相同，尾部修改只影响长行尽头。

收尾9共享资产、受影响JS语法、文档声明、UTF-8/LF与git diff --check通过。历史全量2373断言快照仍过期，不用于本批结论。
