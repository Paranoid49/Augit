# 原生模态打开/关闭交接证据（2026-11-25）

对应整改记录：[docs/modal-transition-flicker-fix-2026-11-25.md](../../docs/modal-transition-flicker-fix-2026-11-25.md)。

本机环境：Windows 11 x64 `10.0.26200`，2880×1800@200%，WebView2 `158.0.4370.33`，.NET SDK `10.0.201`。
基线提交 `5794f32`；整改后构建 0 警告 0 错误。

## 目录

| 路径 | 内容 |
| --- | --- |
| `light-192/`、`dark-192/`、`light-144/`、`light-120/`、`final-smoke/`、`light-192-cycles3/` | 整改后真实外壳逐帧验收 `result.json`（浅/深色、DPI 矩阵与 3 周期连续开关） |
| `negative-prefix-1/`、`negative-prefix-2/` | 基线构建失败的关闭淡出证据（`blend-transition`） |
| `negative-prefix-3/` | 基线构建失败的打开黑帧证据（`dark-run`、状态数不符） |
| `before-open-black-frame.png` | 基线：打开时主窗整块黑帧 |
| `before-close-fade-frame.png` | 基线：关闭时半透明旧表单叠在主界面快照上 |
| `after-open-transition.png`、`after-close-transition.png` | 整改后：打开/关闭均为单帧过渡，主界面像素不变 |
| `pycharm-reference/` | 本机 PyCharm 2026.2.1 参考帧与事件（Settings 打开/关闭） |

`result.json` 中的 `recording` 字段指向本批 FFV1 原始录像；录像单组约 200~430MB，按 `docs/development-validation.md`
和仓库清理约定不入库、不随证据提交。判据可由 `tools/audit/verify-modal-transition-frames.cjs` 重新生成：

```powershell
dotnet build src/Augit.Shell/Augit.Shell.csproj --no-restore --no-incremental /nodeReuse:false
node tools/audit/verify-modal-transition-frames.cjs <Playwright 模块路径> <新输出目录> `
  --ffmpeg <ffmpeg.exe> --theme Light --cycles 2
```

PyCharm 参考实例使用 `D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe`、
`reference-2026.2.1.properties` 与隔离 fixture；`Ctrl+Alt+S` 打开 Settings、`Esc` 关闭。

## 判据摘要

- 稳定状态必须严格按「主界面 → 对话框 → 主界面…」交替且数量为 `2×周期数+1`；
- 相邻稳定状态的过渡帧必须等价于某一端（否则为混合/闪烁），近黑帧拒绝；
- 对话框期间对话框外像素（含 48 物理 px 阴影余量）与打开前/后主界面一致；
- 关闭后主界面与打开前差异 ≤0.5%；
- `result.json` 同时记录临时目录删除、实例退出和关闭方式。
