# 真实前台 OS 鼠标点击 Continue Rebase（2026-10-24）

> 结论：两文件 rebase 冲突、外部删除一个并编辑/暂存另一个后，应用显示 `No unresolved conflicts`、
> `Continue Rebase` 可用；用**真实前台 Win32 鼠标**点击按钮，页面收到 `pointerdown`/`click`
> （目标 `BUTTON.primary-button`，CSS 约 `1209.7,608`）后冲突窗关闭，rebase 完成。
> `git status` 空、分支 `feature`、HEAD 为 `test: feature-second`，日志包含 `feature-second/
> feature-conflict/main/base`。未使用页面 locator 点击或外部 `git rebase --continue`。

## 1. 夹具与构建

- 隔离临时仓库：`main` 与 `feature` 同时修改 `a.txt`/`b.txt`；`feature` 另有 `other.txt`。
- 正常入口：`tools/start-augit.ps1`，强制构建；窗口 PID 19288、端口 56754。
- 初始冲突窗只有 Abort/Skip，无 Continue；外部 `git rm a.txt` 后列表刷新；随后写
  `b.txt=b-resolved` 并 `git add`，Continue 启用。

## 2. 真实鼠标输入

- 窗口 `MainWindowHandle` 取得实际 HWND（`54855492`），先 `SetWindowPos(HWND_TOPMOST)`，
  再以 `AttachThreadInput` + `SetForegroundWindow` 置前；点击前实测
  `foreground_match=True fg=0x3450744 target=0x3450744`。
- 按钮矩形由页面 `getBoundingClientRect()` 取得；**按截图/客户区校准**：
  物理屏幕坐标 = `(window.screenX + rect.x) * devicePixelRatio`，`(1209.8+0)*1.75≈2117`、
  `(608.1+0)*1.75≈1064`。此前直接用 `ClientToScreen + CSS×DPI` 因 PowerShell 宿主
  DPI 虚拟化而偏移约 120/87px，点击未命中；本批记录该失败路径，不把输入未命中当作按钮故障。
- 用 `tools/audit/click-window.ps1 -Absolute -ScreenX 2117 -ScreenY 1064` 发送
  `SetCursorPos` + `mouse_event(LEFTDOWN/LEFTUP)`；点击前 `windowFromPoint` 为
  `Chrome_RenderWidgetHostHWND`（WebView2 子窗口，属于 Augit 的 WebView2 进程），不是另一个应用。
- 页面命中计数：`pointerdown=1`、`click=1`，`document` 捕获到的目标为
  `BUTTON.primary-button`，客户端坐标 `(1209.7,608)`；随后冲突窗从 DOM 移除。

## 3. Git 结果（真实 Git 状态核对）

- `git status --porcelain`：空。
- `git branch --show-current`：`feature`。
- `git log --oneline --all`：`07d3bd6 test: feature-second`、`e819eff test: feature-conflict`、
  `6d73be9 test: main`、`e46603c test: base`。
- `git show --stat --oneline HEAD`：`07d3bd6 test: feature-second`，`other.txt | 1 +`。
- 实例随后 `CloseMainWindow` 正常退出，端口无监听，临时目录删除，无残留进程。

## 4. 证据

```powershell
node tools/audit/conflict-rebase-multifile-continue-os.cjs `
  <playwright> artifacts/conflict-rebase-multifile-continue-os-2026-10-24j
```

- `report.json`：`os-mouse-continue`（HWND、topmost、前台、按钮矩形、点击输出、
  `afterClick.documentHits`）、`ready-to-continue`、`external-resolve-state`。
- `ready-continue.png`、`after-os-click.png`。
- `cleanup.json`：关闭方式、端口关闭、临时目录删除。
- 同批失败样本 `...-24c/d/e/f/g/h/i` 保留，用于区分输入未命中、前台被抢和坐标校准错误；
  其中 `g/h/i` 的 `windowFromPoint match=False` 是 WebView2 子窗口的正常结果，不是点击失败证据。

## 5. 边界

- 只点击一次 Continue；未验证 Skip/Abort、反向解决顺序、二进制冲突、深色/窄窗。
- 未采集参考 PyCharm 同状态整窗像素；页面级仍 50、像素 0。
- 鼠标坐标依赖当前 175% DPI、单显示器；跨屏/其他 DPI 仍需校准。
