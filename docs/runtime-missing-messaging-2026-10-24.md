# 便携包缺少运行时提示与安装器运行时校验（2026-10-24）

> 结论：便携包在缺少 .NET 10 时由 .NET 应用宿主显示缺失项和 `Download it now` 下载按钮；
> 缺少 WebView2 时由 Augit 启动探测显示明确的缺失项、微软官方 Evergreen 下载地址与
> “便携版不自动安装运行时”的说明，然后退出码 1，不崩溃、不留下窗口。
> `VerifyRuntime.ps1` 的哈希/大小/签名成功与失败分支全部按预期返回。安装器在**真实缺失运行时**
> 下的确认/联网下载/取消/失败/重试仍未实测：本机运行时齐全，且不允许卸载共享运行时制造条件。

## 1. .NET 缺失（受控环境变量）

- 不修改系统运行时目录；只给子进程设置 `DOTNET_ROOT`/`DOTNET_ROOT_X64` 指向空目录，
  启动结束立即恢复父进程环境。
- 启动 `Augit.exe` 后，UIA 读到标题 `Augit.exe`、类 `#32770` 的宿主对话框：
  - 文本：`You must install .NET Desktop Runtime to run this application.`
  - 按钮：`Download it now`、`查看详细信息`、`关闭`。
- 退出码 `-2147450749`（0x80008083，宿主无法解析目标框架），没有进入 Augit 窗口。
- 截图：`artifacts/runtime-missing-2026-10-24/dotnet-missing-dialog.png`。
- 说明：应用 runtimeconfig 只声明 `Microsoft.NETCore.App`；宿主提示使用 “Desktop Runtime”
  是 .NET 宿主的文案。安装任一 .NET 10 x64 运行时即可满足，README 已按实测文案说明。

## 2. WebView2 缺失（受控环境变量）

- 设置 `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` 指向空目录，模拟找不到 WebView2 运行时；
  不卸载系统 WebView2。新增启动探测 `ShellStartupProbe.HasWebView2Runtime()` 在创建窗口前检测。
- UIA 读到 Augit 消息框（`ControlType.Text` 完整原文）：

```
Augit needs the Microsoft Edge WebView2 Runtime, or its bundled loader is missing, to show its interface.
Install the Evergreen WebView2 Runtime from Microsoft:
https://developer.microsoft.com/microsoft-edge/webview2/
Portable builds never install runtimes automatically. Start Augit again after installing it.
```

- 进程在点击“确定”后退出，无崩溃；截图：
  `artifacts/runtime-missing-2026-10-24/webview2-missing-fallback-dialog.png`。
- 该路径有自动化文案测试（`ShellFailureTextTests`），但“真实机器未安装 WebView2”的端到端
  仍以本条受控环境变量证据为准，不冒充干净机器验证。

### 2.1 首次最终包烟测发现的回归与修复

- 首次从提交 `a931a1c` 构建的便携包启动时只弹出 `#32770` 对话框并退出 1，没有主窗口。
  原因是启动探测在 `ShellWindow` 显式 `LoadLibrary(native\WebView2Loader.dll)` 之前调用
  `GetAvailableBrowserVersionString()`；P/Invoke 在应用根目录找不到 `native` 子目录里的加载器，
  探测把“加载器未加载”误判成“运行时缺失”。
- 修复：`ShellStartupProbe` 先按与 `ShellWindow` 相同的路径显式加载 `native\WebView2Loader.dll`
  （缺失时返回 false），再探测运行时；重复 LoadLibrary 只增加模块引用计数。
- 回归：Debug 正常启动出现 `Augit` 主窗、`CloseMainWindow` 退出码 0；缺少 WebView2 的受控条件
  仍显示上面的新文案并退出 1；最终便携包重新构建后由 `verify-portable-package.ps1` 复验。
- 该回归没有进入任何对外产物；它证明“只验证 bin/Release 或只验证构建”不足以发现分发路径问题。

## 3. `VerifyRuntime.ps1` 分支

用系统自带的 Microsoft 签名文件与一个第三方签名文件验证，不下载新包：

| 场景 | 期望 | 实测 |
| --- | ---: | ---: |
| Microsoft 签名 + 正确 SHA256 + 正确大小 | 0 | **0** |
| 错误 SHA256 | 10 | **10** |
| 错误大小 | 9 | **9** |
| 未签名文件 + 正确哈希 | 11 | **11** |
| 第三方签名（Git for Windows，`CN=Johannes Schindelin`）+ 正确哈希 | 12 | **12** |

输出：`artifacts/runtime-missing-2026-10-24/verify-runtime-results.txt`。

## 4. 仍未验证

- 安装器在缺少 .NET/WebView2 的干净机器上的确认页、联网下载、哈希/签名校验失败提示、
  用户取消、下载失败和重试。当前只能验证校验脚本分支与便携包提示。
- 安装器安装锁定的 .NET `10.0.11` 是否能在 Windows 10 22H2 上完成，本机无 Windows 10 实机。
- 未卸载或修改系统的 .NET/WebView2 运行时。
