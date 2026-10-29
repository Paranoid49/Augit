# Augit 1.0.0-rc.9 发布说明

- 发布日期：2026-10-29
- 状态：**公开预发布候选（GitHub Pre-release）**，不是正式 1.0
- 构建源提交：以本次 `v1.0.0-rc.9` 标签指向的提交为准，资产 ProductVersion 内含该提交号
- 发布记录 / tag：`v1.0.0-rc.9`
- 平台：Windows 10 22H2 x64、Windows 11 x64

## 1. 这是什么

rc.9 集中修复窗口交互与品牌识别，继续保留 rc.8 的 PyCharm 风格分支弹层：

- **非模态弹层点击外部关闭**：全局搜索、项目树/引用/提交右键菜单、Git 分支筛选、分支弹层、标题栏主菜单和原生 `<details>` 下拉点击外部关闭；若点击位置下方就是按钮、树行、标签或链接，关闭后这次点击继续作用于该控件，不吞点击。模态对话框仍保持 PyCharm 的遮罩/取消行为。
- **标题栏菜单按 PyCharm 行为**：点击左上角三条杠后隐藏三条杠，只显示 **File / View / Git**；Settings 收进 File，移除顶层 Terminal；鼠标移到入口即切换下拉，指针完整离开标题栏/菜单区域后整体恢复；动作菜单定位在入口下方，不再落到窗口底部。
- **项目名 project widget 下拉**：点击左上角当前项目名不再打开单独的 Open Workspace 窗口，而是显示当前项目、完整路径、Open Workspace…、Clone Repository… 和 Recent Projects；点击外部或 Esc 关闭。
- **正文行号栏按 PyCharm 复原**：行号左对齐、右侧竖线、行号栏改用编辑区底色（深色主题不再用工具面板底色），正文与竖线留出间距，代码末尾保留 12px 底部留白。
- **Augit 自有徽标**：蓝底渐变圆角方形 + 白色 A 字标 + 内圈描边；同一图形用于标题栏、工作区 chip、对话框标题、exe/任务栏/Alt-Tab 图标、安装器向导和网页 favicon。任务栏不再显示通用白色页面图标。
- rc.8 的分支弹层分组树（默认折叠、目录分组、搜索、固定高度滚动）继续保留。

## 2. 下载

<!-- RELEASE_ASSETS -->

发布页：<https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.9>

- **便携版**：自带 .NET 10.0.11 Runtime，不需要预装 .NET；只要求系统已有 Microsoft Edge WebView2 Runtime。
- **安装版**：按需联网的小型安装器，需要管理员权限；缺少 .NET 10/WebView2 时先说明，经确认后下载并校验。
- 两种包共用 `%LOCALAPPDATA%\Augit` 中的个人设置。
- Augit 自身未做代码签名，SmartScreen 可能提示未知发布者；请用 `SHA256SUMS.txt` 核对下载完整性。

## 3. 验证摘要

- Release 构建：0 警告、0 错误，`dotnet format --verify-no-changes` 通过。
- 测试：Core **72/72**、Shell **271/271**、Infrastructure **207/207**；托管 CI 排除 4 个需要交互式控制台的 ConPTY 用例，这些用例在真实桌面/Win10 22H2 验证。
- 本地实际运行验证：
  - 非模态弹层点击外部关闭并穿透到下层按钮，全屏覆盖层补发 click 恰好一次；
  - 标题栏三条杠菜单只显示 File/View/Git，Settings 在 File 内，hover 切换、离开恢复，弹层不再落到底部；
  - 项目名下拉显示当前项目/路径/Open/Clone/Recent，独立 Open Workspace 窗口不再出现；
  - 行号左对齐、右侧竖线与正文间距按截图核对；
  - exe 图标提取成功，窗口 `WM_GETICON` 大/小图标句柄均非 0，任务栏实拍显示新 Logo。
- 实际 Release 资产的匿名下载、哈希、便携包启动和安装器版本资源核对结果见[发布清单](https://github.com/Paranoid49/Augit/blob/main/docs/release-manifest-1.0.0-rc.9.md)。

## 4. 已知限制

完整清单见[已知限制](https://github.com/Paranoid49/Augit/blob/main/docs/known-limitations-1.0.0-rc.9.md)。重点：

- **未代码签名**，SmartScreen 可能提示未知发布者。
- 分支行的完整 PyCharm 行内 `NextStep` 子菜单尚未实现；分支行动作走右键/菜单键/Shift+F10。
- 任务栏仍显示旧图标时，需要完全退出 Augit 后重新启动，必要时取消固定再重新固定以刷新 Windows 图标缓存。
- Win10 22H2 实机复验待完成；若仍异常退出，请附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段或 Windows 应用程序日志事件。
- 干净虚拟机、真实缺 WebView2、三屏/物理拔线未验证。
- 整窗像素历史结果仍为 0 张合格 / 50 组页面级配对；500ms 历史慢样本、Settings 567ms、80,000 行软换行、离屏全文连续朗读等边界继续披露。
- 不提供 Force Push、子模块、平台接口、Git Console、自动更新或遥测。

## 5. 反馈

通过 [GitHub Issues](https://github.com/Paranoid49/Augit/issues) 反馈。请附版本、复现步骤、期望/实际结果；异常退出时附 `%LOCALAPPDATA%\Augit\logs\augit.log` 末段（本机诊断日志，不上传；不包含文件内容或凭据）。