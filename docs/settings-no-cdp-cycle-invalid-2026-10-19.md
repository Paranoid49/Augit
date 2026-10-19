# 无 CDP Settings 采样失败记录（2026-10-19）

## 1. 结果

两次 `settings-no-cdp.cjs` 都没有取得可用的 Settings 开关样本，**不能用于 2904ms/500ms 或任何性能结论**。

### cycle1

- PID 11864，正常入口；
- `record.signal` 创建后 10s 录像已结束，未能在有效窗口内点击 Settings；
- 只得到主窗画面，无设置窗。

### cycle2

- PID 9360，正常入口；
- 在录像内点击了 `(2605,50)`，但该坐标命中顶栏 **Search** 放大镜，不是 Settings 齿轮；
- 4s 帧显示 `Open File` 浮层，设置窗从未打开；
- 录像 25s、564MB，只能作为“误点 Search”的失败证据。

### cycle3

- PID 8204，正常入口；
- 点击 `(2700,50)` 后 4s 帧显示的是 **DSH Web GUI**，不是 Augit；说明点击时 Augit 已不在前台，坐标落在了其他应用；
- 设置窗从未打开；
- 录像 25s、约 514MB，不能作为 Settings 性能证据。

## 2. 结论

- 两轮都未到达设置窗，不能提供 `first content`、`close` 或 GDI/DDA 时序；
- 不把“窗口出现”“录像帧”替代设置窗首内容；
- 后续需用实际 Settings 坐标（顶栏齿轮约在 Search 右侧）并在录像开始后立即点击，或复用既有 30 窗样本。

## 3. 证据

- `artifacts/pixel-followup-2026-10-19/settings-no-cdp-cycle1/`（约 704MB，无设置窗）；
- `.../settings-no-cdp-cycle2/`（约 564MB，`frame-4s.png` 显示 Open File）；
- `frame-4s.png` 仅证明误点，不是设置页。

## 4. 边界与清理

- 未改产品代码；页面级仍 **50**、像素仍 **0**。
- 无残留 Augit/PyCharm；临时设置副本由工具清理；全局设置仍 `B8868855…DC3D`。
- 未提交、未推送；`cleanup-check-187.json`。
