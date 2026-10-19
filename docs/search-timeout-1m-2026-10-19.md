# 100 万文件搜索：仍未触发 timeout（2026-10-19）

## 1. 设置

- 250 个目录 × 4000 个 64B 文件 = **1,000,000** 个文件；
- 正常入口 PID 20628，隔离 settings；
- 真实点击正确搜索轨 `(32,257)`，打开 `Find in Files`；
- 查询 `needle_not_present_anywhere`，回车，等待 20s。

## 2. 结果

- 20s 后显示 **`No results found`**；
- **未出现 timeout 提示**；
- 1M 文件树创建约 111s，搜索在 20s 内完成，删除约 215s。

## 3. 与 250k 样本合并

- 250k：18s 内 `No results found`，无 timeout；
- 1M：20s 内 `No results found`，无 timeout；
- 搜索入口坐标已验证为 `(32,257)`，此前 `(32,362)`/头部按钮均不是仓库文本搜索。

## 4. 边界

- 仍没有精确记录查询开始/完成时刻；
- 文件数量继续放大成本很高，且未触发 timeout；不应继续盲目加文件数；
- 若要继续，需要慢文件系统/受控延迟或确认 timeout 的实际触发条件；
- 不能据此判断 timeout 分支不存在或失效。

## 5. 证据

- `artifacts/pixel-followup-2026-10-19/search-timeout1m/after-20s.png`
- `launch.log`

## 6. 清理

- PID 20628 结束；1M 临时树删除（约 214.9s）；无残留 Augit/PyCharm；
- 全局设置仍 `B8868855…DC3D`；未提交、未推送；`cleanup-check-208.json`。
