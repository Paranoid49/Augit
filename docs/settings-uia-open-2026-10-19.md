# Settings 入口 UIA 定位与有效无 CDP 周期（2026-10-19）

## 1. 结论

此前三次无 CDP Settings 采样失败是**坐标问题**：

- cycle2 `(2605,50)` 命中顶栏 Search；
- cycle3 `(2700,50)` 落在其他应用，帧中为 DSH GUI。

本轮用 UIA 读取真实节点：

```text
type = ControlType.Hyperlink
name = Settings
rect = {2637, 12, 54, 53}   // 屏幕物理坐标
```

点击其中心 **`(2664,38)`** 后，Settings 窗正常打开（截图 `settings-opened.png`）。

## 2. 有效周期

- `settings-no-cdp.cjs` cycle4，正常入口，PID 20068，无 CDP；
- 录像内 4s 帧确认 `Settings — Augit` 已打开（`frame-4s.png`）；
- 25s 录像、543MB，保留为有效设置开关窗口证据；
- `analyze-settings-onset.py` 在探针 rect `[550,180,200,60]` 得到首个变化 **1.198s**，但该区域也可能受主窗内容变化影响；**不作为输入到呈现耗时**，也不替代首内容/关闭耗时。

## 3. 边界

- 本轮只证明入口坐标与设置窗可正常打开，不提供 2904ms/500ms 结论；
- 若要测输入到呈现，需同时记录真实点击时间戳，并用只属于设置窗的探针区域；
- 不修改产品代码；页面级仍 **50**、像素仍 **0**。

## 4. 证据与清理

- `artifacts/pixel-followup-2026-10-19/settings-uia/settings-opened.png`
- `artifacts/pixel-followup-2026-10-19/settings-no-cdp-cycle4/frame-4s.png`
- `.../cycle-screen.mkv`、`cycle-screen.json`、`onset-diagnostic.json`
- 无残留 Augit/PyCharm；全局设置仍 `B8868855…DC3D`；未提交、未推送；`cleanup-check-190.json`。
