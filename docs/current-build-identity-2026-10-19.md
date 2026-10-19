# 当前构建身份与验证（2026-10-19 末）

## 1. 构建

```text
dotnet build src/Augit.Shell/Augit.Shell.csproj --no-restore --no-incremental /nodeReuse:false
已成功生成。0 个警告，0 个错误。
```

- `Augit.exe`：163,328 bytes，SHA256 `1738568C62646D2FCD068A14DF0B52E964CEB47380…`
- `Augit.dll`：SHA256 `FA350BFF1BE592A1CF4C53179CC41670922A0EC19A…`
- ProductVersion（推送后最新构建）：`0.1.0+15600a2dc7ec0ecd48a6248e8dfcfa20a8b3103e`
- FileVersion：`0.1.0.0`

## 2. 当前页面资源

```text
live-data.js       12D57E343FF58F2D9198882F0E7395048BC0E2C076772B1FAD7B55FCBDA41B3D
mockup.js          DA93667AA7F6555D4D1DAE2F9C534A6EEA56053EEB7133193E024ADCA8643C59
mockup.css         339741507C3066193D4D52709942B107CEB890096423ACB5FECD87A221705253
terminal-palette.js 32952A034BEF80248AE47501C50F213BA3D66D09162B7F95DF201303ACE0BAF1
```

## 3. 验证

- frame-buttons **24/24**
- titlebar **12/12**
- document-button-states **60/60**
- terminal-palette **2/2**
- 终端状态栏 **7/7**
- Diff 截断 **5/5**
- Smart Checkout **5/5**
- 终端大字号 **5/5**
- UI 资产字节一致性 **PASS**
- 完整 .NET 测试：**521 通过 / 0 失败 / 0 跳过**（Core 72、Shell 246、Infrastructure 203）

## 4. 推送后冒烟

- 正常入口 PID 6344，`ProductVersion=0.1.0+15600a2…`；
- Terminal 正常打开，真实输入 `Write-Host "SMOKE"` 输出；
- 截图：`artifacts/pixel-followup-2026-10-19/post-push-smoke/smoke.png`；
- 未新增产品代码；仅确认推送后构建可启动、终端可用。

## 5. 边界

- 这是当前构建/测试身份，不是整页像素或整体验收；
- 页面级同态配对仍 **50**，像素合格整页仍 **0**；
- 终端 ANSI 浅色 Blue 已修，其余深色/索引未逐色验收；
- 终端对齐/ANSI 修复已推送 `15600a2`；本文档更新随后提交。

## 6. 清理

- 无残留 Augit/PyCharm；测试浏览器已关闭；
- `cleanup-check-209.json`、`cleanup-check-219.json`。
