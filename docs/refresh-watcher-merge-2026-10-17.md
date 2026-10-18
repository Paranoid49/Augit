# 工作区观察器合并窗口 50→20ms 与刷新基线（2026-10-17）

## 改动

`WorkspaceFileWatcher.MergeDelay` 从 50ms 调整为 20ms。该窗口只用于吸收同一次保存产生的重复文件系统事件；20ms 仍能把连续事件并入同一批，同时把“外部文件变化”到状态读取之间的固定等待减少 30ms。

`GitMetadataWatcher.MergeDelay` 保持 50ms：本批只验证普通文件正文刷新的 500ms 目标；Git 元数据变化的路径未改，避免同时改变两条事件源的行为。

改动文件：`src/Augit.Infrastructure/Files/WorkspaceFileWatcher.cs`；相关测试注释同步。

## 正常入口测量

统一通过 `tools/start-augit.ps1` 强制非增量构建，真实文件触发，GDI/DDA 桌面录像，附 CDP。每次运行 4 轮真实文件写入；`presentationCompletion` 为文件写入起点到最后一个 `window/modal-uncover` 应答的双时钟差值，`observedMilliseconds` 含 25ms 轮询。

| 批次 | 实际加载 Infrastructure SHA256 | 轮次呈现 ms（严格撤盖） | observed ms |
| --- | --- | --- | --- |
| 改动前 | `D95B2F3C…75D9F6AC3` | 501.1 / 422.1 / 392.4 / 402.4 | 508 / 426 / 402 / 419 |
| 改动后 A | `2CCA339F…AF8190F1F` | **495.7** / 361.3 / 366.3 / 338.4 | 502 / 393 / 370 / 351 |
| 改动后 B | `2CCA339F…AF8190F1F` | **496.5** / 364.9 / 381.4 / 372.9 | 504 / 381 / 386 / 390 |
| 改动后 C（DDA） | `2CCA339F…AF8190F1F` | **481.0** / 368.0 / 352.1 / 355.5 | 485 / 387 / 373 / 370 |
| 改动后 D（GDI） | `2CCA339F…AF8190F1F` | **488.5** / 366.3 / 362.1 / 369.5 | 506 / 379 / 376 / 373 |

- 后续四轮在改动后稳定比改动前快约 **30–70ms**；第一轮只快约 5ms，因为第一轮包含遮盖子窗首次创建/捕获的固定成本（cover 约 271–274ms，改动前 257ms，属同量级波动）。
- 第一轮严格撤盖在四批改动后样本中分别为 **495.7、496.5、481.0、488.5ms**，均低于 500ms；对应外部 `observed` 为 502/504/485/506ms。外部 25ms 轮询可产生约 6–17ms 的超出，严格产品侧计时成立，但不能仅凭该轮询宣称“外部可见”始终 <500ms。
- 完整帧比较补做了光标排除：GDI 1363 帧、DDA 273 帧，光标矩形外 **0 个变化帧**；本录制的表单客户区内未检出透明/白帧。限制是只覆盖 585×252 表单客户区、25fps 采样且不代表每个 Present，因此仍不宣称全场景无闪或 500ms 验收完成。
- 改动后四批均无页面错误、无残留进程，临时仓库由脚本删除。\n- 外部 Git 元数据（分支 checkout）：命令 82ms、checkout 完成到界面 branch 更新 **125ms**，正常入口 PID 20572/端口 54491，`CloseMainWindow` 成功；详见[Git 元数据刷新](git-metadata-refresh-2026-10-17.md)。

## 完整帧尝试与限制

本批对 GDI B 批与新增 DDA 批尝试了完整帧比较：分别用 `analyze-static-modal-video.py` 生成 `full-frame-analysis.json`（1363 / 273 帧）。

| 批 | 帧数 | 与首帧 0 差异 | 与首帧 78 差异 | 最大差异位置 |
| --- | ---: | ---: | ---: | --- |
| GDI | 1363 | 691 帧 | 672 帧 | `[218,107,221,133]`，输入框光标 |
| DDA | 273 | 变化帧同样只有光标区 | — | 同一光标矩形 |

两批录像的差异全部落在输入框光标矩形 `[218,107,221,133]`（78 像素）。用 `check-refresh-outside-caret.py` 排除该矩形后，GDI 1363 帧与 DDA 273 帧均为 **0 个变化帧**（容差 RGB 2）。这说明本录制的 585×252 表单客户区在 4 轮刷新中没有出现透明、白底或遮盖子窗穿帮变化；但仍只覆盖该客户区与 25fps 采样，未覆盖完整主窗、边框/阴影及每个 Present，因此不能宣称全场景无闪。
此前历史记录中的 187/191/265 等非光标边缘差异，在 20ms 两批 GDI 1363 帧 / DDA 273 帧中均未再出现；全主窗 511 帧边缘带也为 0。不能把消失单独归因于 `MergeDelay` 或某一改动，仍需更大样本确认。

后续完整帧验证应继续录制拥有独立 `HWND` 的 Settings 模态或完整主窗表面，并把 `window/modal-cover`/`uncover` 与隐藏区间关联；本批的表单客户区结果不能外推到整窗或全场景。


## 全窗边缘带与白帧扫描

新增 `--record-owner` 全主窗录制（2880×1716、GDI、4 轮真实文件写入，`refresh-owner-round8/`），并用 `check-refresh-white-edge.py` 对 511 帧检查固定边缘带（顶部 120、底部 60、左右 16 物理像素）与近白像素：

| 项 | 实测 |
| --- | ---: |
| 帧数 | 511 |
| 近白像素最大 | 4,011,970（整帧 4,942,080） |
| 近白阈值 4,500,000 以上帧 | **0** |
| 与首帧比较的边缘带变化帧 | **0** |
| 本轮严格撤盖（GDI 全窗录制负载下） | 742.4 / 414.7 / 396.8 / 417.0ms |
| 另采 DDA 全主窗 277 帧 | 边缘带 **0 变化帧**；近白最大 4,011,970、4.5M 以上 **0**；严格 715.2 / 429.6 / 441.5 / 439.8ms |
| AF55 复验 GDI 全主窗 510 帧 | 边缘带 **0 变化帧**；近白 4,011,970、4.5M 以上 **0**；严格 541.1 / 460.7 / 418.4 / 424.3ms（全窗录制负载） |

- 全窗边缘带在 4 轮刷新中没有采样到变化，也未检出整帧白闪候选；内容区按设计会变化，未纳入边缘带比较。
- 全窗 GDI/DDA 录制本身增加读盘/编码负载，首轮 742/715ms 不能与轻量的模态区域录制延迟直接比较；产品延迟仍以模态区域四批 495.7/496.5/481.0/488.5ms 为参考。
- 仍只覆盖采样帧（GDI 可能漏帧）与固定边缘带，不等于每个 Present 或全内容区无闪，整体验收不完成。
- 采集门禁补充：当采样目标为所有者主窗、前景是其 owned 模态时，`capture-screen-region.ps1` 允许该同实例 owned 前景覆盖采样区；其他前景遮挡仍拒绝。脚本编码/BOM 门禁通过。

## 证据与身份

证据目录：

- `artifacts/pixel-followup-2026-10-17/refresh-baseline-round2/`（改动前）
- `artifacts/pixel-followup-2026-10-17/refresh-baseline-round2-20ms/`（改动后 A，GDI）
- `artifacts/pixel-followup-2026-10-17/refresh-baseline-round2-20ms-b/`（改动后 B，GDI，含完整帧分析）
- `artifacts/pixel-followup-2026-10-17/refresh-baseline-round2-20ms-dda/`（改动后 C，DDA 完整帧对照）

四批 `mapped-images.json` 的实际映射：Shell 均 `27D29A65…2C26944F`；Infrastructure 改动前 `D95B2F3C…75D9F6AC3`，改动后 `2CCA339F…AF8190F1F`。`cleanup.json` 均记录 `removed=true`，运行结束无 Augit 进程和临时目录。

## 验证

| 范围 | 结果 |
| --- | --- |
| 构建 | 0 警告 / 0 错误 |
| WorkspaceFileWatcher + GitMetadataWatcher | 5 通过 |
| ShellBridge workspace changes | 1 通过 |
| Infrastructure 完整模块 | 203 通过 |
| Shell 完整模块 | 243 通过 |
| 全窗边缘/白帧扫描 | `python -B tools/audit/check-refresh-white-edge.py <证据目录>` | 511 帧；边缘带 0 变化；0 近白候选 |
| `git diff --check` | 通过 |

## 限制与下一步

- 20ms 窗口会增加连续写入时的 flush 频率；现有合批测试与三批正常入口刷新未出现重复应用或错误，但长时间连续写入的压力仍需在后续批次验证。
- 第一轮 GDI 余量 3.5–4.3ms、DDA 19ms，仍不足以作为稳定达标结论；需要更多轮次、整窗/Settings 模态完整帧，确认透明帧没有回退。
- 遮盖子窗首次创建的固定成本仍是第一轮的主要组成；此前“预创建子窗”候选因外框闪变被否证，不重做。
- 本批未回滚、暂存或提交既有混合修改；无新增页面或功能。

## 复验

```powershell
node tools/audit/refresh-daily-file.cjs <playwright 模块路径> artifacts/pixel-followup-2026-10-17/refresh-baseline-20ms --gdi-clock
D:/Python/Python3119/python.exe -B tools/audit/analyze-static-modal-video.py <证据目录>
D:/Python/Python3119/python.exe -B tools/audit/check-refresh-outside-caret.py <证据目录>
```

启动与停止：正常入口 `powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1`；异常时仅停止核实属于本批的 PID。
