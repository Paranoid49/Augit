# Windows 10 多显示器几何修复（2026-10-25）

> 这是[Windows 10 多显示器验证](win10-multimonitor-validation-2026-10-25.md)的后续修复批次，修 D1（Settings 越界）和 D2（恢复尺寸只按主屏收敛）。
> 机器仍是 **Windows 10 Pro 21H2 `10.0.19044.2965`**；两台显示器 2560×1440@100% + 1920×1080@100%，工作区 2560×1400 / 1920×1040。
> 本批修改了 Shell 代码和测试；没有改产品对 CET 的默认策略。
> 代码提交：`9552c69`（`fix(shell): clamp settings and restored windows to monitor work area`）。

## 1. 改动

### D1：Settings 夹到 Owner 所在显示器工作区

- `ShellModalGeometry.CenterAndFit(owner, width, height, work)`：先按 Owner 中心得到设置窗矩形，再用现有 `Fit` 整体夹进工作区；设置窗比工作区大时收缩到工作区。
- `ShellWindow.OpenSettingsWindow` 使用 `TryGetMonitorWorkArea` + `CenterAndFit`；工作区取不到时保留原来的中心摆放。
- `SetWindowPos` 不再固定 `SWP_NOSIZE`，因为夹取可能收缩窗口。

### D2：正常启动按窗口所在显示器恢复

- 新增 `ShellWindowSizing.ResolveStartupPlacementInWorkArea`：尺寸先收敛到目标工作区；当尺寸本身放不下（或刚好铺满）目标工作区时，再把整个还原矩形夹进工作区。尺寸放得下的小窗口保留原来的标题栏可达位置。
- `ShellWindow.CreateWindowInstance` 在正常启动（没有显式 `--width/--height`）时：
  1. 先用 `ResolveStartupPosition` 解析保存位置/回退位置；
  2. 用新增的 `MonitorFromPoint` + `GetMonitorInfo` 取该点最近显示器的工作区；
  3. 调新方法得到最终摆放。
- 显式 `--width/--height` 的审计矩阵仍走 `ResolveStartupPlacement` 和主屏工作区，避免改变已有审计截图。
- 屏外保存位置仍先回退 `(80,80)`，再按主屏工作区收敛。

## 2. 自动化测试

新增 9 个测试，Shell 测试 **262/262 通过**；构建 0 警告 0 错误。

| 测试 | 覆盖 |
| --- | --- |
| `设置窗居中后按工作区右边界回退` | Owner `4200,200` → 设置窗 `3440,244–4480,916` |
| `设置窗居中后按工作区下边界回退` | Owner 靠下 → `2730,368–3770,1040` |
| `子窗大于工作区时收缩到工作区` | 2000×900 → 1024×640 |
| `子窗尺寸非法时拒绝` | 宽/高 0 抛 `ArgumentException` |
| `恢复的大窗口按目标显示器工作区收敛` | 2500×1300 @ `2600,100` → `2560,0–4480,1040` |
| `副屏正常大小的恢复矩形保持不变` | 2660,120/1180×760 不变 |
| `尺寸放得下时保留标题栏可达的屏外位置` | `4000,100` 保持，不把有意跨屏的小窗口挪回单屏 |
| `工作区不可用时回退到原启动摆放` | 保持旧路径 |
| `目标显示器上仍恢复最大化状态` | 最大化标记保留 |

日志：`artifacts/win10-multimonitor-2026-10-25/fix/shell-tests-262.log`。

## 3. 实机复验（修复后）

| 场景 | 修复前 | 修复后 |
| --- | --- | --- |
| D1：Owner `4200,200`，Settings 1040×672 | `4270,244–5310,916`，830px 在屏外 | `3440,244–4480,916`，完整落在工作区，右边缘 4480 |
| D1：Owner `2660,120`，Settings 正常位置 | `2730,164–3770,836` | 不变 |
| D1：Owner 靠下 `2660,780` | 会向下超界 | `2730,368–3770,1040`，下边缘 1040 |
| D2：保存 `2600,100` / 2500×1300，副屏启动 | `2600,100–5100,1400`，620px 右、360px 下越界 | `2560,0–4480,1040`，客户区 1920×1040，CSS 视口 1920×1040 |
| 默认首次启动 | 80,80/1180×760 | 不变 |
| 副屏最大化恢复 | 客户区 1920×1040 | 不变 |

对尺寸放得下的小窗口，仍沿用原来的“标题栏可达即可”规则；只有尺寸本身放不下（或刚好铺满）目标工作区时，才连同位置一起夹取。因此 `4000,100`/1180×760 重启后仍是 `4000,100–5180,860`，不会被强行挪回单块显示器；D2 的 2500×1300 才会被整窗夹取。

截图：

- D1：[d1-after-settings-edge-1040x672.png](../artifacts/win10-multimonitor-2026-10-25/fix/d1-after-settings-edge-1040x672.png)
- D2：[d2-after-oversized-secondary-1920x1040.png](../artifacts/win10-multimonitor-2026-10-25/fix/d2-after-oversized-secondary-1920x1040.png)

## 4. CET 构建修正（重要）

修复过程中重新确认：本机 **默认构建**的 `Augit.exe` 启动即退出 `0x80131506`；之前“App 可以启动”的结论成立，是因为验证构建带了 `-p:CETCompat=false`，并且最后需要**强制全量重建**才会替换 apphost。

- 默认构建（不关闭 CETCompat）：`Augit.exe` 退出 `-2146233082`，事件日志见 `fix/default-build-cet-crash.txt`。
- 本批所有实机 UI 验证使用：`dotnet build ... -p:CETCompat=false --no-incremental`，0 警告 0 错误。
- 这是本机 **Windows 10 21H2 19044.2965 未安装 CET 支持补丁**的平台限制，不等于 22H2 一定失败；22H2 仍需要实机验证。
- 是否把 `<CETCompat>false</CETCompat>` 写进产品、或要求 Windows 10 最低补丁级别，属于发布安全/支持范围裁决，**本批未改**。

因此，上一份验证报告的“`Augit.exe` 本身可正常启动”已在本批修正为“仅在 CET 关闭构建下可启动”。

## 5. 未完成

- D3（模拟 175% 默认位置压任务栏）未改；需要真实 175% 环境再确认。
- 真实混合 DPI/`WM_DPICHANGED`、Windows 10 22H2、安装器/发布产物仍未验证。
- 本批改动文件为 `ShellModalGeometry.cs`、`ShellWindow.Settings.cs`、`ShellWindowSizing.cs`、`ShellWindow.cs` 及两个 Shell 测试文件；D1/D2 修复已提交。

## 6. 证据

- `artifacts/win10-multimonitor-2026-10-25/fix/`：修复后截图、构建/测试日志、默认构建 CET 崩溃日志。
- `fix/fix-measurements.json`：机器可读的修复前后测量与测试结果。
- 原验证报告：[win10-multimonitor-validation-2026-10-25.md](win10-multimonitor-validation-2026-10-25.md)。
