# 25 万文件搜索：正确入口与未触发超时（2026-10-19）

## 1. 正确入口

之前两次点击失败是坐标错误。CDP 诊断只用于读取 DOM，不是验收输入：

```text
.rail-button[data-tool="search"] rect = x0 y127 w37 h40 (CSS)
```

175% 缩放下真实点击中心为 **`(32,257)`**，不是此前用的 `(32,362)`。

## 2. 设置

- 250 个目录 × 1000 个 64B 文件 = **250,000** 个文件；
- 正常入口 PID 10228，隔离 settings；
- 真实点击 `(32,257)` 打开 `Find in Files`；
- 输入 `needle_not_present_anywhere`，回车，等待 18s。

## 3. 结果

- 搜索浮层正常打开，查询文本正确；
- 18s 后显示 **`No results found` / “Try another query or adjust the filters.”**；
- **未出现 timeout 提示**。

因此 25 万个小文件在该机器上可在 18s 内完成搜索；本轮没有触发搜索超时，不能据此判断 timeout 分支不存在或失效。

## 4. 边界

- 没有精确记录搜索完成时刻；“18s 内完成”不等于 15s 阈值内完成；
- 不再重复 250k 文件路径；若要继续，需要更大/更慢文件系统，并记录查询开始与 timeout/完成时间；
- 页面级仍 **50**、像素仍 **0**；本轮不增加配对计数。

## 5. 证据

- `artifacts/pixel-followup-2026-10-19/search-timeout2/after-18s.png`
- `launch.log`

## 6. 清理

- PID 10228 已结束；250k 临时树删除（约 52.5s）；无残留 Augit/PyCharm；
- 全局设置仍 `B8868855…DC3D`；未提交、未推送；`cleanup-check-207.json`。
