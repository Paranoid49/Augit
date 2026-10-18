# 刷新首个可见新内容帧（2026-10-18）

## 目的与口径

此前“外部可见”以最后一次 `window/modal-uncover` 应答为代理，无法回答撤盖后多久才真正看到新内容。本批在正常入口、全主窗 GDI 录像上，把同一轮时间线拆为：

1. 外部文件写入前（事件 `started`）；
2. `document/read` 应答结束；
3. `window/modal-refresh` 应答结束；
4. `window/modal-uncover` 应答结束（原严格代理）；
5. 录像裁切区域中首个与静置新内容完全一致的真实屏幕帧。

测量工具：`tools/audit/measure-refresh-content-onset.py`。它使用 `correlate-refresh-clock.py` 的 GDI 墙钟关联；裁切 `x=760,y=200,w=380,h=500`（编辑正文左侧，避开 Go to Line 模态和底部工具窗），容差 16、旧内容变化阈值 100 像素、新内容差异阈值 0 像素。新旧内容在该区域差 1248–1898 像素，能区分 generation 变化。

## 实测

正常入口 `tools/start-augit.ps1` 强制非增量构建，PID 10204、端口 56646，临时工作区与设置副本均隔离；Shell `Augit.dll` SHA256 `159123F2…EA8559D`、Infrastructure `A92BBE4C…D42894CE`。录像 2880×1716、约 20 fps、视频 SHA256 `423F6325…F5B25E`。

| 轮次 | 写入→读取结束 | →refresh 结束 | →uncover 结束（严格） | →首个新内容帧 | 严格后滞后 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 54.0ms | 634.0ms | 651.6ms | **731.7ms** | +80.1ms |
| 2 | 37.0ms | 408.0ms | 421.7ms | **494.7ms** | +73.0ms |
| 3 | 35.0ms | 381.0ms | 402.5ms | **462.5ms** | +60.0ms |
| 4 | 37.0ms | 369.0ms | 390.4ms | **480.2ms** | +89.8ms |

- 首个新内容帧与首个变化帧相同，说明该裁切区域直接出现新 generation，不是先出现旧内容快照再替换。
- 录像约 510 帧/25s（≈20.4fps，平均 49ms/帧），因此“撤盖后 60–90ms”是**采样上界**；真实屏幕内容可能早 0–49ms，即约 11–90ms，不能把全部滞后都归因于产品。
- 即便如此，“最后一次撤盖应答结束”仍不能当作首个真实内容帧；第 1 轮严格 651.6ms、采样首内容 731.7ms，即使扣掉一整帧 49ms 仍约 682ms，超过 500ms。第 2–4 轮采样首内容 462.5–494.7ms，扣一帧后仍可能低于 500ms，但该组 GDI 负载不能代表无扰动正常使用。
- 同一构建另跑轻量严格计时（无全主窗录像，PID 4888、端口 60701）：严格 `460.9/339.6/352.8/359.5ms`，全部低于 500ms；该组没有全窗视频，不能与上表混为同一口径。

## 证据与复验

- 全窗录像证据：`artifacts/pixel-followup-2026-10-18/refresh-owner-current/`，含 `events.json`、`recording.json`、`recording.log`、`refresh-content-onset.json`、`content-onset-*-pre/final/first.png`。
- 轻量严格计时：`artifacts/pixel-followup-2026-10-18/refresh-current/`。
- 复验：

```powershell
python -B -m unittest tools/audit/test_refresh_content_onset.py
python -B tools/audit/measure-refresh-content-onset.py artifacts/pixel-followup-2026-10-18/refresh-owner-current
node tools/audit/refresh-daily-file.cjs <playwright> artifacts/pixel-followup-2026-10-18/refresh-owner-current --gdi-clock --record-owner
```

## 边界

- 只覆盖编辑正文左侧裁切区域；裁切外内容、未采样帧和 GDI 漏帧不在结论内。
- 录屏本身抬高首轮；本轮第 1 轮不能代表无录像时的用户延迟。
- 仍未覆盖所有 Present、全部 Git 元数据、不同 DPI、不同负载和整窗无闪；500ms 整体验收仍未完成。
