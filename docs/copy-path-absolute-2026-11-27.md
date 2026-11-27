# Copy Path/Reference：绝对路径复制（2026-11-27）

## 1. 参考实机

- 安装：`D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe`，`build.txt` = `PY-262.9437.214`；
- 隔离配置：`PYCHARM_PROPERTIES=D:/JetBrains/PyCharm-2026.2.1-reference/reference-2026.2.1.properties`，fixture 为同目录 `fixture`；
- 实测入口：Project Files 里 `app.py` 行右键 → 顶层 `Copy Path/Reference...` → 二级菜单：
  - `Absolute Path`（快捷键显示 `Ctrl+Shift+C`，右侧预览完整绝对路径）
  - `File Name`
  - `Path from Content Root`
  - `Path From Repository Root`
  - `Toolbox URL`
- 参考证据：`artifacts/copy-path-absolute-2026-11-27/pycharm-copy-path-submenu.png`；
  菜单源码对照 `platform/platform-impl/resources/idea/PlatformActions.xml` 的 `CopyFileReference` 组
  （`CopyAbsolutePath`、`CopyFileName`、`CopyContentRootPath` 等）与
  `platform/lang-impl/src/com/intellij/ide/actions/CopyPathProvider.kt` 的
  `CopyAbsolutePathProvider` / `CopyContentRootPathProvider`。

## 2. Augit 实现

- 项目树和 Commit 改动列表右键菜单的 `Copy Path/Reference…` 改为二级菜单：
  - `Absolute Path`：当前工作区绝对路径；工作区根来自宿主 `workspace/info` 的 `live.root`，
    Windows 分隔符统一为 `\`，树根节点本身已是绝对路径时原样复制；
  - `Path from Content Root`：保留原有项目内相对路径复制；
  - 二级项右侧显示将复制的值（参考实机的路径预览列）。
- 交互：悬停或 `ArrowRight`/`Enter` 展开；子菜单贴近窗口右缘时向左翻并夹在窗口内；
  `Esc` 先收起二级菜单，再按一次才关顶层菜单；复制仍走宿主 `clipboard/write`，
  失败提示保留原因与“文件内容/改动未改变”。
- 视觉：顶层触发器右侧 16px 展开折线；子菜单复用弹出层背景、边框、8px 圆角与阴影，
  标题 `Copy` 居中，子项行高 28px、不画图标列；静态视觉稿通过 `:hover/:focus-within` 展开。
- 与参考的登记差异：只实现产品需要的 `Absolute Path`、`Path from Content Root` 两项；
  `File Name`、`Path From Repository Root`、`Toolbox URL` 没有对应产品能力或会重复，
  不显示占位项；不复制 PyCharm 的 `Ctrl+Shift+C` 快捷键（Augit 快捷键按产品规格另行管理）。
- 顺带修正：视觉稿的 Changes 样例菜单在实时外壳里不再与真实菜单叠开两层。

## 3. 验证

- 无头 `live-shell` 定向块：**27 项断言通过**，覆盖：
  - 项目树/Changes 顶层菜单条目不变，触发器标签为 `Copy Path/Reference…`；
  - `Path from Content Root` 写入相对路径，`Absolute Path` 写入 `D:\live-ws\…`；
  - 两级菜单键盘路径、`Esc` 逐级收起、右缘向左翻且不出窗口；
  - 原有复制失败提示、菜单夹边、文件历史等回归。
- 真实外壳：项目树右键 `.github` → `Absolute Path`，宿主剪贴板实际读回
  `D:\github\Augit\.github`；Changes 菜单同样出现两个路径项；截图见
  `artifacts/copy-path-absolute-2026-11-27/augit-project-copy-path-submenu.png`
  与 `augit-changes-copy-path-submenu.png`。
- 参考进程已关闭（0 个 `pycharm64`），fixture 保持探测前状态（`M app.py`、`?? .idea/` 为既有状态）；
  测试实例已退出、临时 WebView2 profile 已删除、系统剪贴板已恢复原内容。
- 文件哈希与实测状态见 `artifacts/copy-path-absolute-2026-11-27/result.json`。
