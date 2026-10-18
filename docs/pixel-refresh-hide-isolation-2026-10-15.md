# 背景刷新隐藏与捕获隔离（2026-10-15）

## 结论与边界

将遮盖后隐藏表单与 CapturePreview 调用分开，六轮均在隐藏期间保留200ms再恢复。该等待只存在审计脚本，不修改产品延迟。两组本次采样均只有角点变化，未复现正常刷新曾出现的整面正文泄漏；不能据此宣布修复。

证据根目录为 artifacts/pixel-followup-2026-10-15/refresh-hide-isolation：

| 模式 | 有效帧/变化帧 | 最坏变化 | 背景版本 | 结论 |
| --- | --- | --- | --- | --- |
| hide-only | 53/28 | 12像素，最大通道差6，仅角点 | 六轮均0→0 | 无边缘或正文变化，整表面严格门禁仍失败 |
| capture-graceful | 69/43 | 12像素，最大通道差6，仅角点 | 六轮均0→0 | 追加捕获调用但图像未替换，整表面严格门禁仍失败 |

两组整表面1820×1341、容差2、不排除任何像素；hide-only SHA256为8664c9c95beb1da4441e2e96345cb318c71c00fc8b8a4da12fca0f802a9c3c52；capture-graceful为08fdc08d2ef841c8de29646e04d7c4aecb9d36119c1319cab1b7b99059cc3b69，最大采样间隔1014ms，无重复时间戳。事件式采集仍不是完整Present追踪，不能用无采样变化证明所有时刻稳定。

捕获分支桥接序列为 modal-cover、modal-refresh(depth=0)、modal-uncover。ShellModalHost.RefreshSnapshotAsync 会调用实时控制器 CapturePreviewAsync，但 ReplaceAsync 对相同PNG不增加版本。此试验没有改变底层内容，因而尚未分离“捕获本身”和“新背景图加载/替换”的影响；下一步应隔离图像替换时序，不再反复尝试同一显隐顺序。原有正常刷新DDA正文泄漏证据仍有效。

## 采集器修复与验证

首次 with-capture 分支因静止桌面不再产生新帧，录像未走到媒体时间12秒，30秒墙钟保护终止进程，退出null，门禁拒绝。保留失败视频和日志，没有伪造清单、补帧或计作通过。

现对DDA开放专用stdin，在六轮完成后等待700ms并发送FFmpeg正常结束命令q，正常封装原始帧；仍保留30秒异常兜底。GDI保持原先采集参数。新增结束命令定向测试；录屏参数与隔离恢复共8项通过。capture-graceful 正常退出，严格整表面检查退出1，结果如上。解码输出的rawvideo时间基舍入警告未导致删除帧，源录像无重复时间戳。

产品 ShellModalHost.cs 未改，SHA256仍为77B43F8E4C009DFFB3C15288B2D88CEC8BCB5537D137557893C138841E330B9A；未构建、未运行全量套件。跳转行产品修改另见 pixel-goto-line-2026-10-15.md。

## 复验与清理

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node --test tools/audit/refresh-recorder-arguments.test.cjs tools/audit/refresh-cover-isolation.test.cjs
node tools/audit/capture-shadow-shell.cjs $pw artifacts/refresh-isolate-new Light --refresh-video --desktop-duplication --isolate-cover-capture
python -B tools/audit/verify-refresh-surface.py artifacts/refresh-isolate-new --require-stable
# 正常由finally清理；异常只停止本次日志返回的PID及其子树。
taskkill /PID <本次Augit或录屏PID> /T /F
```

独立检查Augit PID21112/25196/25180、录屏25260/20256/21120均不存在；54387/54873/57742无监听；专用临时目录gsOBbd/o3PfQD/WypN6U均已删除。未触及用户既有进程和历史被拒绝目录，正式证据保留。Windows10排除，跨屏DPI未验证，总体目标仍未完成。
