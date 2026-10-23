# PyCharm 参考窗口的 JAB / UIA 可达性结论（2026-10-24）

> 结论：在本机 PyCharm 2026.2.1 reference 上启用 JBR 自带 Java Access Bridge 后，
> UIA 能识别顶层 `SunAwtFrame`，但**仍只枚举到 JBR 标题栏控件**；MSAA client 也只有
> 1 个 child（标题栏）。Java 内容树（菜单、Push、Remotes）仍不可枚举，因此
> Define Remote 的参考实机反馈位置/生命周期本轮仍无法通过 UIA/MSAA 自动取证。

## 1. 操作

- 使用 JBR 自带开关（不是系统 `jabswitch`）：
  `D:\JetBrains\PyCharm 2026.2.1\jbr\bin\jabswitch.exe -enable`
- 启动参考：
  `D:\JetBrains\PyCharm-2026.2.1-reference\unpacked\bin\pycharm64.exe`
  + `reference-2026.2.1.properties` + fixture。
- PID 30380，窗口标题 `notes.txt – README.md`。
- UIA：
  ```text
  root.Name=notes.txt – README.md
  root.ClassName=SunAwtFrame
  descendants=1
  child[0]=ControlType.Pane class=JBRCustomTitleBarControls
  ```
- MSAA `AccessibleObjectFromWindow(OBJID_CLIENT)`：
  - `accChildCount=1`
  - `accName(0)=notes.txt – README.md`
  - 无 Java 菜单/按钮子节点。
- 尝试 `jaccesswalker.exe`：打开的是 GUI 窗口，不提供可脚本化的树输出；已关闭。

## 2. 结论与边界

- JAB 已启用但 Java 内容仍没有通过 UIA/MSAA 暴露；这不是“窗口不存在”，而是
  AWT/JBR 无障碍桥在本机没有把 Java 控件挂到可遍历树。
- 不能据此点击参考的 `Define Remote`，也不能读取参考通知位置；不编造参考截图或
  通知文本。
- 参考侧仍保持**未验证**，不是“参考没有该反馈”或“Augit 已与参考一致”。
- 本轮已恢复：PyCharm PID 30380 正常关闭；JBR JAB 已 `-disable`；无 pycharm64/
  jaccesswalker 残留。
- 这是参考可达性结论，不是产品行为或像素通过；页面级配对仍 50、像素仍 0。

## 3. 复验注意

- 不同 JBR/PyCharm 版本、屏幕阅读器或 `idea.support.screen.readers` 可能改变 Java
  内容树；本次没有把该属性写入用户配置，也未修改安装目录。
- 若后续需要参考通知几何，优先使用可脚本化的 Java 侧辅助（例如测试插件/内部动作），
  而不是继续重复 UIA/MSAA 轮询。
