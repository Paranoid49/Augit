# 大文件 Warning 真实输入与持久性闭环（2026-10-19）

## 1. 结论

在隔离夹具中用**正常入口**打开 28,500,000 字节、300,000 行的 `huge.txt`，使用 Windows `SendInput`/`SetCursorPos` 的真实鼠标点击，完成了第 142 批留下的三个缺口：

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| Warning 横幅可见 | 通过；显示 `Large file (27.2 MB). Showing the first 2.4 MB as a read-only preview.` 与两个动作 | `actual/huge-opened-2.png` |
| 真实鼠标点击 **Hide Notification** | 通过；横幅最终消失，`hideLargeFileWarning=false` 未被写入 | `actual/after-hide-realclick.png`、`actual/hide-timing-after.png` |
| Hide 后关闭/重开同一文件 | 通过；重开后横幅仍不出现 | `actual/tab-closed-2.png`、`actual/reopened-after-hide-2.png` |
| Hide 不跨重启 | 通过；正常关闭并重启后横幅重新出现，说明它只属于本次会话 | `actual/relaunch2-initial.png` |
| 真实鼠标点击 **Don't Show Again** | 通过；`hideLargeFileWarning=true` 写入隔离设置，横幅消失 | `actual/after-disable-final.png` |
| Don't Show Again 跨重启 | 通过；重启后同一 `huge.txt` 直接打开且横幅不出现 | `actual/restart-after-disable.png`、`actual/settings-after-disable-and-restart.json` |
| 参考端同文件 Warning | **未出现**；参考 2026.2.1 以普通编辑器打开同一 28,500,000 字节文件，无横幅、无两个动作 | `reference/reference-huge-open.png` |

页面级配对仍为 **48**，像素合格整页仍为 **0**。本批不修改代码，不改变计数。

## 2. 身份与方法

- 分支 `dsh`，HEAD `da5728992a44811c118d4f6382a3b3a15c9ce5aa`。
- 正常入口命令：

  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1 `
    -Workspace D:/github/Augit/.tmp/warning-closure-2026-10-19/fixture `
    -SettingsFile D:/github/Augit/.tmp/warning-closure-2026-10-19/settings.json
  ```

- 实际实例 PID 5288 / 14836 / 14456 / 18724，均通过 `Alt+F4` 正常关闭；未启用 CDP，未使用 DOM `element.click()`。
- 窗口 2880×1716，DPI 168（scale 1.75），浅色主题。
- `Augit.exe` SHA256 `1738568C…23CC`；`Augit.dll` `FA350BFF…AAB7`；`ProductVersion=0.1.0+da572899…ce5aa`。
- `live-data.js` `12D57E34…1B3D`、`mockup.js` `F3D0CD53…671D`、`mockup.css` `53BC7DF7…1A6C`，与正常入口基线一致。
- 横幅按钮在物理坐标 `Hide Notification=(2555,240)`、`Don't Show Again=(2745,240)`；点击由系统鼠标输入完成。参考/实际截图均由 DWM frame `CopyFromScreen` 采集。

## 3. 时序更正

第 142 批把物理合成点击未命中归为“工具限制”，这个解释**不准确**。本批第一次真实点击 Hide 后，1.2s 截图里横幅仍在，但后续截图里横幅已经消失；随后关闭并重开同一文件，横幅仍隐藏，证明 Hide 动作确实被真实点击触发。

为了不让“1.2s 观察点”冒充响应耗时，又做了一次独立真实点击，并以屏幕像素 `(1500,240)` 的颜色变化计时：

- 点击前横幅底色 `255,246,233`；
- 点击后到 3739ms 的最后一次采样仍是该颜色；
- 首次变白的采样在 4231ms；屏幕像素变化落在 **3739–4231ms** 内。

因此本样本的可见反馈约为 **3.7–4.2s**，不是 1.2s 内完成。采样循环本身有约 0.1–0.5s 的间隔，不能把 4231ms 当成更精确的单一耗时。该延迟是本次 28.5MB 文件（2.5MB 只读预览）上的实际观察；没有性能门限规范，本批**不据此判缺陷**，也不宣称即时响应。若后续要优化，应单独检查隐藏通知是否必须重绘整段预览。

## 4. 参考端

- 参考实例：`D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe`，`product-info.json` 为 **PyCharm 2026.2.1 / build PY-262.9437.214**，PID 23072。
- 同一 28,500,000 字节文件复制到参考隔离 fixture 后，在 Project 树双击打开；编辑器直接显示正文，顶部没有 `File is too large` 横幅，也没有 `Hide Notification` / `Don't Show Again`。
- 没有为了制造警告去修改参考的 `idea.properties`、注册表、实验特性或禁用标志；参考的默认状态就是本批记录的状态。
- 因此“参考端 Warning 显示及动作”在本轮**没有可配对的对应 UI**。这不是把实际端横幅判为缺陷，而是把参考缺失登记为产品/版本差异；实际端的横幅与动作按 `ux-spec` §7.5 单独验证。

## 5. 边界

- 只覆盖浅色、DPI 168、28,500,000 字节文本、单次隐藏/重开/重启样本；未覆盖深色、其他 DPI、其他扩展名、图片大文件、二进制摘要。
- Hide 的 3.7–4.2s 是屏幕可见变化区间，不是 DOM 状态改变时间；不能把两者混写。
- Don't Show Again 的设置在隔离文件中验证为 `true`，重启后仍为 `true`；真实用户设置未被触碰（`B8868855…DC3D`）。
- 参考没有警告动作，故本项不能增加页面级配对或像素合格数。

## 6. 清理

- 实际 PID 5288 / 14836 / 14456 / 18724 均已正常关闭；参考 PID 23072 经 Confirm Exit 关闭。
- 临时夹具与隔离设置目录已删除；参考 fixture 中的 `huge.txt` 已删除。
- 参考 `config/options` 在启动前做了快照，结束后恢复并与快照逐文件 SHA256 比对一致；未覆盖用户现有设置。
- 实际全局 `%LOCALAPPDATA%/Augit/settings.json` 仍为 `B8868855…DC3D`。
- 本批未修改代码、未提交、未推送。
- 证据目录：`artifacts/pixel-followup-2026-10-19/large-warning-closure/`；清单 `evidence-manifest.json`；清理记录 `cleanup-check-165.json`。
