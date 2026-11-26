# 原生模态打开闪黑与关闭淡出整改（2026-11-25）

## 1. 结论

用户报告「打开 Format JSON 窗口时主界面会闪烁」。逐帧录屏确认两个用户可见缺陷：

1. **打开时主窗整块闪黑 3~6 帧（60Hz 下约 50~100ms）**：新建的背景截图 WebView2 控制器在首图就绪前以全尺寸透明表面参与合成，把无画刷所有者窗口画成黑块。
2. **关闭时约 160ms／10 帧的半透明旧表单淡出**：自定义模态窗口被系统 DWM 隐藏过渡淡出，旧表单半透明叠在所有者快照上，违反 `docs/ux-spec.md` §5.3「原生模态窗口打开和关闭都不显示透明帧或下层快照闪动」。

两者都在共享的 `ShellModalHost` 原生模态宿主里，覆盖 Format JSON、New Branch / Rename Branch、Push、Reset、Stash、Worktree、远端管理、Rollback 等全部普通模态。整改后本机 200% DPI、浅/深色、模拟 144/120 DPI 的逐帧审计均通过，旧构建在同一审计下稳定失败。

## 2. 环境与基线

| 项 | 值 |
| --- | --- |
| 系统 | Windows 11 家庭中文版 `10.0.26200` x64 |
| 显示 | 单屏 2880×1800，200% 缩放（工作区 2880×1704，CSS 1440×852） |
| 运行时 | .NET SDK `10.0.201`，WebView2 Runtime `158.0.4370.33` |
| 参考 IDE | 本机 PyCharm `2026.2.1`（build `262.9437.214`，独立 reference 配置与 fixture） |
| 基线 | `dsh` 分支，提交 `5794f32`，工作区干净 |
| 构建 | `dotnet build src/Augit.Shell/Augit.Shell.csproj --no-restore --no-incremental /nodeReuse:false`，0 警告 0 错误 |

本批证据执行身份（每个 `result.json` 的 `executable` / `assemblies` / `webAssets` 字段逐次记录）：

- 整改后构建：`Augit.exe` `33835B4614D53F14…`、`Augit.dll` `C0B9D1846AB7…`、`Augit.Core.dll` / `Augit.Infrastructure.dll` 同批；
- 基线负向构建：`Augit.dll` `CF9314A3ECB4…`（仅还原 `ShellModalHost.cs` / `ShellSnapshotPresentation.cs` 后重建）；
- 网页身份：`web/src/live-data.js` `272F4F74…`、`web/src/mockup.js` `4A17B45B…`、`web/src/mockup.css` `3EF8ECCC…`（本批未改网页）。

## 3. 复现

隔离实例（不读写用户设置与 WebView2 配置），按下 `Ctrl+Shift+J` 打开 Format JSON，用 Desktop Duplication（ffmpeg `ddagrab`，60fps，FFV1 无损）录窗口物理区域 2360×1520：

```powershell
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS='--remote-debugging-port=9350'
Augit.exe --web-root D:/github/Augit/web --workspace D:/github/Augit `
  --settings-file <临时目录>/settings.json --user-data-folder <临时目录>/webview `
  --theme Light --width 1180 --height 760 --no-session-restore
# CDP 连接后发送 Control+Shift+J，同时开始录屏；关闭用 Escape。
```

基线构建逐帧结果（同一录制内两个开关周期）：

- 打开阶段出现稳定黑帧：`mean≈22.0~22.3`、`darkFraction=0.911864`，连续 2~3 帧；首轮实测 3~6 帧。见 `artifacts/modal-transition-2026-11-25/before-open-black-frame.png`。
- 关闭阶段出现约 16~18 个混合帧（每个中间帧同时明显不同于关闭前与关闭后画面），旧表单以半透明状态压在主界面快照上，持续约 160ms。见 `before-close-fade-frame.png`。
- 负向验收记录：`negative-prefix-1/result.json`（`dark-run` `mean=22.347 darkFraction=0.911864`，状态数 `7` ≠ `5`）、`negative-prefix-2/result.json`（16 项 `blend-transition`）、`negative-prefix-3/result.json`（18 项）；三次均为基线构建 `Augit.dll` SHA256=`CF9314A3…`。

## 4. 根因

### 4.1 打开闪黑

打开模态时，宿主先创建**背景截图控制器**（第二个 `CoreWebView2Controller`，父窗为主窗）作为主界面的静态副本，并把业务控制器移入模态窗。基线代码在创建背景控制器后立即：

- 保持 `IsVisible=true`（WebView2 默认）；
- `DefaultBackgroundColor=Transparent`；
- 把 Bounds 设为完整父客户区。

此时 PNG 尚未 onload。透明 WebView2 不会透出下层的业务控制器，而是露出所有者窗口未绘制的客户区，DWM 呈现为整块黑色；直到背景图加载并参与合成才恢复，形成 3~6 帧黑闪。

尝试过的两条「隐藏」路线都不可用，已用真实宿主验证：

- `IsVisible=false`：WebView2 停住渲染，背景页 onload 后的两帧 `requestAnimationFrame` 确认永远不返回，模态呈现挂起；
- 把控制器移到父客户区裁剪区外：同样被判定为不可见，呈现挂起。

### 4.2 关闭淡出

模态窗是普通 Win32 顶层窗口。系统 DWM 在隐藏该窗口时执行隐式过渡：在约 10 帧内把旧窗口内容以递减不透明度叠到所有者窗口（背景快照）上。规格明确禁止透明帧；本机 PyCharm 参考实机是用自身遮罩动画过渡独立对话框，而不是把旧表单淡出到下层快照（见 §7）。

## 5. 实现

改动集中在 `src/Augit.Shell/ShellModalHost.cs` 与 `src/Augit.Shell/ShellSnapshotPresentation.cs`：

1. **首图就绪前停放背景控制器**：新增 `Snapshot.ParkForFirstFrame(scale)`，在构建背景控制器后把它缩成父客户区左上角的 `2×2` 物理像素（`ShellSnapshotPresentation.ParkedSize`）。控制器仍参与屏幕合成，因此渲染与 rAF 不被节流；2px 在屏幕上不可察觉（录屏中逐帧核对未产生可见像素变化）。
2. **显示背景副本并完成合成屏障后才移走业务控制器**：新增 `Snapshot.ShowBackgroundAsync(parent, scale)`，把控制器移入完整客户区，调用 `CapturePreviewAsync` 强制一帧并 `DwmFlush`。呈现路径在 `_live.ParentWindow = window` 之前 await 该屏障：主窗先完成「实时界面 → 像素相同的背景副本」交接，再移走业务控制器，不再露出无子窗黑底。屏障失败只记录，不阻止呈现。
3. **过期呈现检查**：背景屏障等待期间用户可能关闭/重开窗口，增加 `_disposed || !_operations.IsCurrent(generation)` 检查，避免把已过期窗口继续当成当前表面重挂。
4. **禁用模态窗系统过渡**：模态窗创建后设置 `DWMWA_TRANSITIONS_FORCEDISABLED = 1`，关闭时旧表单立即消失，不再有半透明淡出。模态窗自身已有 DWM cloak + 首帧屏障，不需要系统过渡。
5. 背景图替换路径本批尝试过「新图叠旧图后再移除」的候选，因为实验证明刷新瞬变并非该替换造成，且会增加并发状态风险，**已撤销**，未进入本提交。

所有普通模态共用该宿主，因此修复一次覆盖全部同类窗口。

## 6. 验证

### 6.1 新增真实外壳逐帧审计

新增可复用工具：

- `tools/audit/verify-modal-transition-frames.cjs`：隔离启动真实 Shell → CDP 驱动 Ctrl+Shift+J/Escape → `ddagrab` 60fps 录像 → 两遍解码 → 逐帧判据；
- `tools/audit/modal-transition-analysis.cjs`：纯计算判据；
- `tools/audit/modal-transition-analysis.test.cjs`：12 项单元测试（区域计数、稳定段、状态分类合并、混合帧、黑帧、框外变化、状态顺序等）。

判据：

- 稳定状态必须严格按「主界面 → 对话框 → 主界面…」交替，数量必须等于 `2×周期数+1`；
- 相邻稳定状态之间的过渡帧必须等价于某一端（否则判为半透明混合/闪烁）；近黑帧判为黑闪；
- 对话框期间，对话框矩形（含 48 物理 px 阴影余量）以外的像素必须与打开前/后主界面一致（容差 RGB 2，允许 0.2% 的合法状态文字更新）；
- 关闭后主界面与打开前差异不超过 0.5%；
- 同一会话内重复开关的每个周期都按上述判据检查。

命令（Playwright 模块路径与 ffmpeg 显式传入）：

```powershell
node tools/audit/verify-modal-transition-frames.cjs `
  <Playwright 模块路径> artifacts/modal-transition-2026-11-25/light-192 `
  --ffmpeg <ffmpeg.exe 路径> --theme Light --cycles 2
```

### 6.2 验收矩阵（整改后构建）

| 用例 | 主题 / DPI | 周期 | 分析帧 | 过渡帧 | 对话框外变化 | 关闭后差异 | 结果 |
| --- | --- | ---: | ---: | --- | --- | --- | --- |
| `light-192` | Light / 200% | 2 | 1032 | `234,286,721,780`（均为单帧，等价端点） | 0 / 54 px | 0.0241% | PASS |
| `dark-192` | Dark / 200% | 2 | 1002 | `239,291,685,742` | 0 / 62 px | 0.0277% | PASS |
| `light-144` | Light / 150% 模拟 | 2 | 813 | `186,233,556,613` | 0 / 75 px | 0.0335% | PASS |
| `light-120` | Light / 125% 模拟 | 2 | 817 | `188,236,548,601` | 0 / 0 px | 0% | PASS |
| `final-smoke` | Light / 200% | 2 | 892 | `224,268,637,680` | 0 / 0 px | 0.0241% | PASS |

其中 54/62/75 px 是第二次打开前状态栏有一次合法文字刷新（同一批改变同时出现在打开前/后主界面，判据取前后较小差异）；不是背景错位。每个用例的 `result.json` 均记录 `temporaryRemoved=true`、`appExited=true`、`closeMethod=CloseMainWindow`。

### 6.3 负向验证（基线构建）

把 `ShellModalHost.cs` / `ShellSnapshotPresentation.cs` 还原到基线后重新构建，同一工具：

| 用例 | 结果 | 关键证据 |
| --- | --- | --- |
| `negative-prefix-1` | FAIL | `dark-run run=7 start=520..522 mean=22.347 darkFraction=0.911864`；状态数 `7` ≠ `5` |
| `negative-prefix-2` | FAIL | 16 项 `blend-transition`（关闭淡出） |
| `negative-prefix-3` | FAIL | 18 项 `blend-transition` |

即旧实现的两个缺陷都能被同一套判据稳定捕获，整改后不再出现。

### 6.4 其它模态与窗口

- **紧凑输入窗（New Branch）**：本机临时驱动打开 Git Log → 提交右键 → New Branch，8 个稳定状态、相邻过渡均为单帧，无黑帧/混合帧。
- **嵌套模态（Stash 管理器 → Delete Stash 确认）**：临时 git 仓库含 1 个 stash；5 个稳定状态（主界面 → Stash → 删除确认 → Stash → 主界面），过渡 `1/1/2/1` 帧，无黑帧/混合帧。
- **Settings 独立窗口**：主界面在设置打开/关闭期间保持原画面不变；设置窗口自身在首次内容前有 2~3 帧内部空白/加载画面（独立窗口路径、既有行为，本批未修改）。PyCharm 参考实机同样在对话框内部显示 `loading...` 占位。
- 以上临时驱动未入库；`Format JSON` 的逐帧证据由入库审计工具可复现。

## 7. PyCharm 参考实机观察

使用 `D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe`、`reference-2026.2.1.properties` 与隔离 fixture，`Ctrl+Alt+S` 打开 Settings：

- Settings 是独立 `SunAwtDialog` 窗口；主框架内容始终可见，没有被清空、替换为快照或出现黑帧；
- 对话框先显示自身的 `loading...` 占位，再加载内容；
- 关闭后主框架保持原内容；观察到的变化集中在对话框内部及其自身过渡，不存在「旧表单淡出到主界面快照」的系统隐藏过渡。

结论：参考实机支持「打开/关闭模态不能导致主界面闪烁或重建」这一产品要求；Augit 按自身规格（遮罩透明、不显示透明帧）选择直接禁用系统整窗过渡，不宣称动画与 PyCharm 逐帧一致。

证据帧：`artifacts/modal-transition-2026-11-25/pycharm-reference/`（`pycharm-main-frame-100.png`、`pycharm-dialog-open-frame-300.png`、`pycharm-dialog-open-frame-760.png`）。原始录像 429MB 未入库。

### 7.1 intellij-community 源码对照

- `platform/platform-impl/src/com/intellij/openapi/ui/impl/DialogWrapperPeerImpl.java`：`createDialog(...)` 用 `new MyDialog(OwnerOptional.findOwner(owner), wrapper, project)` 创建 owned `javax.swing.JDialog` 并调用 `setModalityType(...)`（约 258~266 行；`MyDialog` 类声明约 286 行）。主框架与对话框是两个独立原生窗口，主框架自己的内容不重挂、不截图、不参与切换。
- `platform/platform-api/src/com/intellij/openapi/ui/DialogWrapper.java`：`IdeModalityType.toAwtModality()` 映射到 AWT 模态（`APPLICATION_MODAL` 等，约 158~178 行）；模态期间由 AWT 禁用所有者，但主框架原窗口内容继续在原位合成。关闭走对话框自身的隐藏/销毁，没有「把旧对话框内容淡出到主框架快照」的步骤。
- `intellij-community` 中没有针对 DWM 显示/隐藏过渡的特殊处理（在 `platform/` 等源码中检索 `DwmSetWindowAttribute`/`TRANSITIONS_FORCEDISABLED` 无结果）——参考实现依赖「独立窗口 + 主框架不重建」的拓扑天然满足这一点。
- Augit 的拓扑是「同一个业务 WebView2 被裁切重挂到独立模态窗，所有者显示背景快照」，因此必须显式完成背景首帧交接并禁用系统整窗过渡，才能达到参考实机「主界面不黑、不重建」的结果；本批不复制 intellij-community 的窗口拓扑，只对齐其用户可见结果。

## 8. 测试与检查

| 检查 | 结果 |
| --- | --- |
| `dotnet build Augit.slnx --no-restore` | 0 警告 0 错误 |
| `dotnet test tests/Augit.Shell.Tests --no-build --no-restore` | 285/285 通过（13s） |
| `node --test tools/audit/modal-transition-analysis.test.cjs` | 12/12 通过 |
| `dotnet format Augit.slnx --verify-no-changes --no-restore` | 退出码 0 |
| `powershell -NoProfile -File tools/audit/verify-ui-assets.ps1` | PASS（未改 web 资源） |
| `powershell -NoProfile -File tools/audit/verify-script-encoding.ps1` | PASS（34 个 PowerShell 脚本） |
| `git diff --check` | 通过 |

## 9. 限制与已知残留

- 实机只覆盖 Windows 11 x64；Windows 10 22H2 未在本机验证。
- 144/120 DPI 用 `--dpi` 固定栅格化比例模拟，不等同真实系统缩放切换；真实跨屏/DPI 变化（`WM_DPICHANGED`）未在本批逐帧录像。
- **背景刷新残留（既有问题，本批未修）**：模态打开期间外部文件变化触发背景捕获/刷新时，背景快照层仍有 2~4 帧瞬变；基线构建同样存在（同一场景基线帧 `mean=213.712` 瞬变），不属于本次打开/关闭交接修复范围。历史上持续跟踪的刷新边缘闪帧记录见 `docs/pixel-refresh-surface-2026-10-15.md`、`docs/refresh-status-batch-2026-10-17.md` 等。
- 设置窗口首次内容前的 2~3 帧内部空白/加载画面未修（独立窗口路径，主界面不受影响）。
- 本批未优化打开总耗时（背景捕获 + 首帧屏障仍使首次呈现约 2s），不把消除闪烁等同于性能验收。

## 10. 资源清理

- 审计脚本每个实例使用独立临时设置与 WebView2 profile，`finally` 中停止 ffmpeg、关闭浏览器、`CloseMainWindow` 关闭实例并有界重试删除临时目录；`result.json` 记录 `temporaryRemoved=true`、`appExited=true`。
- 临时采集用的 `Augit.exe`、`ffmpeg.exe` 与 PyCharm 参考实例均已退出，未残留监听端口；临时 git 仓库与 `.tmp` 驱动目录已删除。
- 原始录像为 FFV1 无损、单组约 200~430MB，未入库；入库的是脱敏后的 `result.json`、关键帧 PNG 与 PyCharm 参考帧。

## 11. 证据清单

`artifacts/modal-transition-2026-11-25/`：

- `light-192/result.json`、`dark-192/result.json`、`light-144/result.json`、`light-120/result.json`、`final-smoke/result.json`：整改后逐帧验收；
- `negative-prefix-1/result.json`：基线打开黑帧失败证据（`dark-run`、状态数不符）；
- `negative-prefix-2/result.json`、`negative-prefix-3/result.json`：基线关闭淡出失败证据（`blend-transition`）；
- `before-open-black-frame.png`、`before-close-fade-frame.png`：基线缺陷关键帧；
- `after-open-transition.png`、`after-close-transition.png`：整改后过渡关键帧；
- `pycharm-reference/`：参考实机主界面帧、Settings 打开帧、`events.json`。

## 12. 2026-11-26 补记：关闭后主界面掉到其它窗口后面/闪烁

用户报告关闭原生模态后主界面会短暂掉到其它应用后面，或闪一下才回来。复核关闭路径发现：模态存续期间所有者窗口被 `EnableWindow(owner,false)`；原实现先 `ShowWindow(modal,0)` 隐藏活动模态，再等待 WebView2 首帧，最后才启用并激活所有者。隐藏活动窗的同步请求会让系统先把前台交给其它应用；随后 `SetForegroundWindow(owner)` 可能被前台锁拒绝，主界面就留在后面或出现一次切换闪烁。

修复：`ShellModalHost.CloseAsync` 在隐藏最上层模态前先启用所有者并 `SetForegroundWindow(owner)` 再 `DwmFlush`；嵌套层 `Pop` 同样先激活父窗再隐藏子窗；全部清理结束后再次兜底激活所有者。设置独立窗口此前已是相同顺序（先 owner-enabled/foreground，再 cloak/hide）。

本机真实外壳验证：后台保留 Notepad 窗口，打开 Format JSON 后关闭；以 5ms 间隔采样前台窗口 240 次，关闭后全部样本都属于 Augit 主窗，未出现 Notepad 或其它进程前台。覆盖同一共享 `ShellModalHost` 的 Format JSON、Rollback 等全部普通模态与嵌套退出路径。
