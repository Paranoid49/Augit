# 当前构建正常入口身份（2026-10-18）

## 1. 目的

确认第 3–15 批 UI 修改后，正常入口实际构建、Web 资源和窗口身份，避免用历史哈希代替当前加载身份。本记录只证明正常入口与身份，不改变页面配对/像素验收计数。

## 2. 启动与窗口

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1 `
  -Workspace D:/JetBrains/PyCharm-2026.2.1-reference/fixture
```

- 构建：0 警告 0 错误；PID **13864**，启动时间 2026-10-18 20:32:15。
- 窗口：标题 `Augit`，最大化 2880×1716、DPI168、scale 1.75；桌面采集 `foregroundBeforeAndAfter=true`。
- 证据：`artifacts/pixel-followup-2026-10-18/normal-entry-identity/normal-entry.png`（SHA256 `56d24943…b799`）。
- 未启用 CDP；`settings.json` 启动前/后 SHA256 均为 `B8868855A535D49EFD0D89FC98E78359DB8BD0AAAD3D1A1FA887A6740BE8DC3D`。

## 3. 身份

| 文件 | SHA256 |
| --- | --- |
| `Augit.exe` | `DA0C4AFD3CF898ECCD961D0F1DFA19776B4605494D0FFDB9BB390C92240DF96C` |
| `Augit.dll` | `F53764A81EF29A81BC78E6921E8C0B9D529DE059B5037A94EB5E8A6F6AEB4E00` |
| `web/src/mockup.js` | `F3D0CD530D2B017538A21B3ECC43F8D7CF8E13BC4AC598AA4BF4A187A7E7671D` |
| `web/src/mockup.css` | `53BC7DF736BA3801C3529E0BE48D30F7D29FA4906F19A0F8F9A49E1C810C1A6C` |
| `web/src/live-data.js` | `12D57E343FF58F2D9198882F0E7395048BC0E2C076772B1FAD7B55FCBDA41B3D` |

- 2026-10-18 第 46 批：修复 Unified Diff 上下文行号；第 50 批修复 Blame 未提交行空白归属；第 57 批修复 Stash 两复选同行，`mockup.js` 当前 `DDF905A9…9377`、`mockup.css` `EBEAE830…4D6B`；正常入口 PID 5464 已加载并实采。`Augit.exe`/`Augit.dll` 未变。
| `web/src/terminal-copy.js` | `A6F9FEA483D8877BA4B815C125750EBA6A67985EC9A2C9DDE4D5FA40CB04604F` |

- `ProductVersion = 0.1.0+690eb1937b4b65600dbbd9d3e4bc91b8f93a61dd`，`FileVersion = 0.1.0.0`。
- 第 17 批终端边色修改后，正常入口启动 PID 15736（`mockup.css` `64D8983F…`）；第 20 批选择色后正常入口 PID 9056（20:43 选词采集）；第 25 批终端复制修复后正常入口 PID 12644；第 27 批抽出 `terminal-copy.js` 纯函数并用 PID 23864 复验，`live-data.js` 第 70 批紧凑输入空名修复后 `FC04772F…17B1`，第 99 批项目根节点右键回落修复后 `B7A1F7DF…AD7F`，第 131 批推送成功 toast 修复后 `12D57E34…1B3D`（正常入口 PID 12592 已加载并采根节点菜单）；第 103 批设置初始脏修复后 `mockup.js` `F3D0CD53…671D`（正常入口 PID 18236 验证 Terminal Apply 禁用/无蓝点）；第 136 批曾误加 File View 等宽字号输入（`3745BF98…1002`），第 137 批按 2026-10-15 用户裁决回退，正常入口 PID 4852 复验 File View 只读展示，`mockup.js` 回到 `F3D0CD53…671D`；`terminal-copy.js` `A6F9FEA4…`、`mockup.css` 第 69 批后 `5BAA1232…8967`，第 140 批大文件 Warning 横幅网格修复后 `53BC7DF7…1A6C`（正常入口 PID 14720 横幅可见）；exe/dll 不变。正常入口 PID 3308 已加载该身份（New Tag 空态）。PID 13864 是第 16 批 `40A05586…` 的前一身份。
- `mockup.css` 哈希已不同于第 2 批的 `20EFA054…12E7`：包含 Local 标签、设置左栏/控件/页顶、深色 accent 与编辑器底色等修改；运行时资产与视觉稿字节一致（`verify-ui-assets.ps1` PASS）。
- 深色编辑器底 `#1E1F22` 由 `verify-ux-offline-icons.cjs`（含 text-viewer 深浅断言）与深色状态配对覆盖；本正常入口冒烟为浅色，不单独证明深色整页。

## 4. 边界与清理

- 本记录不证明 54 页/全模态、设置首内容/白闪、刷新 500ms 或跨屏 DPI；这些仍按各自证据。
- PID 13864 正常关闭；设置恢复 `B8868855…DC3D`；无端口/应用残留；`cleanup-check-46.json`。未提交、未推送。

## 5. 2026-10-19 接续批次身份

- 分支 `dsh`、HEAD `da5728992a44811c118d4f6382a3b3a15c9ce5aa`；正常入口强制非增量构建 0 警告 0 错误。
- `ProductVersion=0.1.0+da5728992a44811c118d4f6382a3b3a15c9ce5aa`。
- `Augit.exe` SHA256 `1738568C62646D2FCD068A14DF0B52E964CEB47380C7F4B4D3BC1D45357523CC`；`Augit.dll` `FA350BFF1BE592A1CF4C53179CC41670922A0EC19AA9A59998179455E43EAAB7`。
- `live-data.js` `12D57E343FF58F2D9198882F0E7395048BC0E2C076772B1FAD7B55FCBDA41B3D`；`mockup.js` `F3D0CD530D2B017538A21B3ECC43F8D7CF8E13BC4AC598AA4BF4A187A7E7671D`；`mockup.css` `53BC7DF736BA3801C3529E0BE48D30F7D29FA4906F19A0F8F9A49E1C810C1A6C`。
- 正常入口实例 PID 5288 / 14836 / 14456 / 18724，2880×1716、DPI 168、浅色；均未启用 CDP，均通过 `Alt+F4` 正常关闭。本批用于大文件 Warning 真实鼠标与重开/重启验证，详见[真实输入与持久性闭环](large-warning-real-input-2026-10-19.md)。
- 实际全局设置未变，仍 `B8868855…DC3D`；本批未修改代码。

## 6. 2026-10-19 终端状态栏修复后身份

- 分支 `dsh`、HEAD 仍 `da5728992a44811c118d4f6382a3b3a15c9ce5aa`；正常入口强制非增量构建 0 警告 0 错误。
- `ProductVersion=0.1.0+da5728992a44811c118d4f6382a3b3a15c9ce5aa`；`Augit.exe` `1738568C…23CC`、`Augit.dll` `FA350BFF…AAB7` 未变。
- `live-data.js` 仍 `12D57E34…1B3D`；终端状态栏高度修复后：
  - `mockup.js` `DA93667AA7F6555D4D1DAE2F9C534A6EEA56053EEB7133193E024ADCA8643C59`；
  - `mockup.css` `F53F53D88DFED8CE844FC80FB67AEE3945ACBEC62AC5EFA7F4317256D3911EDF`。
- 两份共享资产与 `docs/ux-mockups/` 字节一致（`verify-ui-assets.ps1` PASS）。
- 正常入口实例 PID 452（浅色 1661 `#F1F2F4` / 1662 `#E9EAEE`）、4672（深色 1660 `#191A1C` / 1661 `#212326` / 1662 `#26282C`）逐像素复验；2880×1716、DPI 168；均未启用 CDP，正常关闭。详见[终端状态栏高度与顶边修复](terminal-statusbar-height-fix-2026-10-19.md)。
- 隐藏终端后状态栏上缘回到 1668，普通页面未回归；深色首行/整页仍未重采。
