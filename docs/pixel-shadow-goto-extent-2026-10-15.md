# 跳转行窗口外沿与启动交付复核（2026-10-15）

## 结果及范围

浅色、活动态、175% 系统缩放下，PyCharm 2026.2.1 与 Augit 普通入口的跳转行窗口均为 584×252 物理像素。矩形外侧 192 像素宽的八个互不重叠区域，共 468480 像素，背景逐像素相同，阴影 RGB 最大差异为 0。最外圈 3204 像素两端均回到背景色，未见采样截断。已目视检查双方原图：外沿无鼠标光晕或通知遮挡。

这只证明该尺寸窗口在上述条件下的矩形外沿。圆角矩形内部、深色、非活动态及 Settings 大窗口仍未全部验收，`fullShadowVerified` 保持 false。正文、标签和输入布局不纳入外沿结论；不能据此宣称完整窗口或 55 页配对完成。

上一组 128 像素外沿虽有 279552 像素零差异，但最外圈仍有 617 像素暗化，最大 1 级，不能当作完整末端。Settings 最小尺寸受参考源码 `SettingsDialog.java` 的 900×700 逻辑像素约束，本屏无法容纳其 128 像素完整周边；本批换用小窗口补充证据，没有替代 Settings 验收。

## 采集条件与来源

参考使用隔离 PyCharm fixture 的真实 notes.txt、白色正文，关闭跳转框后原位采两次背景。Augit 从无场景参数的普通入口恢复真实 notes.txt，以产品点击和 Ctrl+G 打开跳转框。审计脚本只为阴影配对注入尺寸及位置，不改变颜色、字体或阴影；此采样不作为未经调整的日常整窗截图。

参考采样区为 `[955,534,968,636]`，窗口边界 `[1147,726,1731,978]`；Augit 采样区 `[1061,693,968,636]`，窗口边界 `[1253,885,1837,1137]`。两端 DPI 168，按窗口左上角整数偏移对应，不缩放原图。

证据根目录为 `artifacts/pixel-followup-2026-10-15/`：

- 参考：`shadow-goto-reference-light-192/`。
- Augit：`shadow-goto-actual-light-192/`，`capture.json` 登记普通入口、模态关闭恢复、配置、进程及背景稳定性。
- 比较：`shadow-goto-actual-light-192/ring.json`，八区域、采样边界和源文件 SHA256 均有记录。
- 文件仍沿用采集器的 `settings.png` 名称，实际对话框由 `dialogKind: goto-line` 标识。

参考前图 SHA256 为 `4f0456dbcf9a7af2a66de282d4f1a638b53179a01a717ab90aea04a03b0b839d`；Augit 前图为 `9b9d90ab1a03c01f2ecb316970cab1c2218747ef4108db78ebd79603022ddf0e`。两端背景及重复背景均为 `9431131faa6735ec20457b91a6d7a8e9b86265af9a05b9a8eab4d162d4f62827`。

## 工具修改与验证

`compare-shadow-ring.py` 新增最外圈回到背景的检查，并以完整外沿几何验证取代不适用于小窗口的内角验证。`capture-shadow-shell.cjs` 新增跳转行采集入口，采样半径上限扩到 256。`test_shadow_ring.py` 覆盖相同但截断的阴影、小窗口与非法参数；`shadow-capture-placement.test.cjs` 补 192 像素放置。未修改产品显示代码。

定向验证：7 项 Python 测试通过（0.145 秒）、4 项放置测试通过（约 134 毫秒）、采集脚本语法通过；共享 UI 资产、Debug/Release 各 24 个随包资源与源码逐字节一致；`git diff --check` 通过。没有重复执行产品全量测试。

```powershell
D:/Python/Python3119/python.exe -B tools/audit/test_shadow_ring.py
node --test tools/audit/shadow-capture-placement.test.cjs
node --check tools/audit/capture-shadow-shell.cjs
$pw = 'C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/capture-shadow-shell.cjs $pw artifacts/pixel-followup-2026-10-15/shadow-goto-actual-light-192 Light --normal-entry --uniform-background --goto-line --reference-frame artifacts/pixel-followup-2026-10-15/shadow-goto-reference-light-192/settings.png.json --place-inside-body --shadow-radius 192
```

重新采集须使用新的输出目录，避免覆盖证据。参考图重新采集需先通过桌面操作打开相应参考窗口，不能拿旧前景句柄直接重放。

早期三次实际采集分别因其他窗口占据前景、定位等待超时、主窗口而非模态处于前景而失败；严格门禁未放宽，未生成可冒充成功的比较。最终使用正文点击后 Ctrl+G 的正式入口成功。一次工具路径转义错误在执行前被拒绝，改用实际枚举的窗口对象恢复；一次壳调用同类错误也在执行前被拒绝。

## 日常启动交付复核

本批重新读取默认配置：Light、界面 Microsoft YaHei UI 12、正文 JetBrains Mono 13、appearanceVersion=1。Debug/Release 随包资源均与当前源码一致。已目视复核 `artifacts/startup-path-investigation-2026-10-15/daily-user-handoff/normal-commit.png`；它是此前直接执行日常脚本的截图，不是本批重新采集的全状态矩阵。32 状态入口对照范围及残差继续以 [启动对齐记录](startup-alignment-2026-10-15.md) 为准。

日常启动：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
```

正常关闭窗口即可退出；必要时只停止脚本输出的本实例：

```powershell
taskkill /PID <启动时输出的PID> /T /F
```

## 清理与当前哈希

参考 PID12764 及其本轮子进程已停止；成功采集 PID18416、端口60316、临时目录 `Augit-shadow-pair-BnVt5U` 已由 finally 清理。独立检查也覆盖前序 PID24736/25212/25200/24092/2884、端口63316/63081/54766/51717、临时目录 vWOqhI/Bt8Tht/uW8mjQ/fS8Qa4，结果均无残留。未重试历史拒绝清理的其他目录。未暂存、提交或回滚混合工作区。

产品四文件 MD5 本批前后不变：

- mockup.js：`6E61081C842CA96770B3F8BB56E13B0A`。
- mockup.css：`99A3D1E61C7D12BA82DD25CE2E685A76`。
- live-data.js：`641B29F3BC50EC22500924F64B9C63CE`。
- bridge.js：`F297BF8769E249CA0D804F4F4EEEF5A9`。

总体整改仍未完成：55 页及全部模态同态配对、刷新全过程、终端/冲突完整逐行配对与其余系统阴影状态继续保留。Windows 10 已按用户裁决排除；跨屏 DPI 保留未验证。
