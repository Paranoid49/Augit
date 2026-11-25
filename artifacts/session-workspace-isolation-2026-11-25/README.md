# 跨工作区标签会话与缺失文件标签证据（2026-11-25）

本目录是 [跨工作区标签会话与已不存在文件的幽灵标签整改](../../docs/session-workspace-isolation-2026-11-25.md) 的取证材料。

## 目录

| 路径 | 内容 |
| --- | --- |
| `before-cross-workspace/` | 整改前：以工作区 A 的会话设置启动工作区 B；`window.png` 可见 `global.json`、`src/a.txt` 两个 `Missing` 幽灵标签，`package.json` 是被恢复的 A 同名文件；`settings-after-start.json` 证明 `lastWorkspace=B` 而 `openFiles` 仍是 A 的三项。 |
| `after-cross-workspace/` | 整改后同一操作：无幽灵标签、无恢复的旧标签；`settings-after-start.json` 中 `openFiles` 已清空，`toolWindows.workspaceRoot` 固定为 A。 |
| `same-workspace-missing/` | 隔离实例（显式 settings 文件）：`1-restored.png` 只恢复仍存在的 `package.json` 并回退激活；`2-explicit-missing.png` 显式打开不存在文件时右下提示原因、不建标签；`3-background-deleted.png` 后台标签被外部删除后保留标签并显示删除状态页；`result.json` 六项检查全部为真。 |
| `pycharm-reference/` | 本机 PyCharm 2026.2.1 参考实机：`1-before-delete.png` 为 `global.json` 已打开；`2-after-delete.png` 为从磁盘删除并触发 VFS 同步后，标签与项目树条目都消失、活动标签落到 `README.md`；`probe.json` 记录进程/PID、窗口矩形与前台核对。 |

## 环境

| 项 | 值 |
| --- | --- |
| 系统 | Windows 11 家庭中文版 `10.0.26200` x64，单屏 2880×1800 @ 200% |
| 运行时 | .NET SDK `10.0.201`，WebView2 Runtime `158.0.4370.33` |
| 构建 | `dotnet build Augit.slnx --no-restore`（0 警告 0 错误） |
| PyCharm | `D:/JetBrains/PyCharm-2026.2.1-reference`，build `262.9437.214`，隔离 `reference-2026.2.1.properties` |
| 参考源码 | `D:/github/intellij-community`（`FileEditorManagerImpl.kt:2116-2123`） |

## 构建与页面身份（SHA-256）

| 文件 | 哈希 |
| --- | --- |
| `web/src/live-data.js` | `AE8A344C3B977BFD048D1497CD6B5C8E96F8C62C26EAE2C1349BD95B64AC11D9` |
| `src/Augit.Infrastructure/Settings/SettingsStore.cs` | `F19519FD941573DB74DD0735953F04ECF16220F3F42F7558CC192CE4590C125D` |
| `Augit.dll`（Debug `win-x64`） | `733061F2419427C1BC91CB38398CA18B77BBD9DC10AA6753635BBDFD2710B37C` |
| `Augit.exe`（Debug `win-x64`） | `375E769B8A32E1C53D8FB676075D3C0F5844F46C1FA950F87342C32B94DB74F2` |
| `tests/Augit.Infrastructure.Tests/SettingsStoreTests.cs` | `9F79F91D2830A606427A1203074A4F45B9BF68DCF6B9CF8E9C587C81F4B97353` |
| `tests/Augit.Shell.Tests/ShellBridgeSettingsTests.cs` | `A98603CC1F7436492642A132A9332B5C94ED7D6E86BB08C1F5E9DD81D2E17876` |
| `tools/audit/live-shell.spec.cjs` | `A7A96A99821F42107AE207F428DC0D652039109566AFBFA8622D28975502F386` |

## 执行方式摘要

- 跨工作区取证走**真实入口**（非隔离实例）：备份用户 `%LOCALAPPDATA%\Augit\settings.json` 并记录 SHA-256，写入工作区 A 的会话，正常启动 `Augit.exe --workspace B`，通过 WebView2 CDP 读取 `window.__augitLive` 的标签/正文状态并截图；进程结束后恢复原设置文件。复现前后用户设置 SHA-256 均为 `0D5315801AC8402DE0DC335E9805A751317979DF861BB5F229A3632B748A5F9F`。
- 同工作区取证使用 `--settings-file` / `--user-data-folder` 隔离实例，避免读写用户设置；隔离目录取证后删除。
- PyCharm 参考在 fixture 临时加入 `global.json`/`package.json`，`pycharm64.exe <fixture> <fixture>/global.json` 打开文件，删除 `global.json` 后用 `Ctrl+Alt+Y` 触发 VFS 同步；截图后停止实例并删除临时文件，fixture `git status` 与取证前一致。

## 结果

- 整改前：`before-cross-workspace/result.json` 的 `tabs` 为 `global.json`（Missing）、`package.json`（TextReady）、`src/a.txt`（Missing），活动文件是被恢复的 `global.json`。
- 整改后：`after-cross-workspace/result.json` 的 `tabs` 为 `[]`，项目树只有 B 的 `package.json`。
- 同工作区：`same-workspace-missing/result.json` 的 `passed` 六项全部为 `true`。
- PyCharm：删除文件并同步后标签关闭；参考源码同样在删除事件中关闭该路径下所有已打开编辑器。
