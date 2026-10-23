# PyCharm 参考：Define Remote 自动 Fetch 失败的反馈（2026-10-24）

> 结论：参考 PyCharm 2026.2.1 在 Define Remote 中对无效 URL 的自动 Fetch 失败，
> **保留 Define Remote 对话框**、URL 边框变红、在 URL 附近弹出错误气球
> `unable to access 'http://127.0.0.1:1/repo.git/': The requested URL returned error: 502`；
> 远端**没有写入** `.git/config`。这与 Augit 当前“关闭 Define、写入远端、管理器内联 notice”
> 的流程不一致，是已定位的呈现/生命周期差距。

## 1. 参考入口

- 参考安装：`D:/JetBrains/PyCharm-2026.2.1-reference/unpacked/bin/pycharm64.exe`；
- `PYCHARM_PROPERTIES=reference-2026.2.1.properties`，fixture 为
  `D:/JetBrains/PyCharm-2026.2.1-reference/fixture`；
- 正常窗口 PID 21940，窗口标题 `notes.txt – README.md`；
- 真实键盘/鼠标：
  1. 点击标题栏使参考窗口到前台（ChatGPT 浮动窗会遮挡，未关闭用户窗口）；
  2. `Ctrl+Shift+A` → 输入 `Manage Remotes` → Enter；
  3. Git Remotes 对话框点 `+`；
  4. Define Remote：Name `origin`，URL `http://127.0.0.1:1/repo.git`，勾选 Fetch remote；
  5. Enter（默认 OK）。

## 2. 结果

- Define Remote 对话框**没有关闭**；
- URL 输入框边框变红；
- URL 上方出现错误气球：
  ```
  unable to access 'http://127.0.0.1:1/repo.git/': The requested URL returned error: 502
  ```
- Git Remotes 管理器仍在其后，但没有（也无需）自己的通知；
- `git -C fixture config --get-regexp '^remote\.'` **无输出**，说明失败时没有写入远端；
- 截图：`artifacts/define-remote-reference-2026-10-24/reference-error.png`
  （原图 2898×1734，PID 21940）。

## 3. 与 Augit 当前实现的差距

| 项 | 参考 | Augit 当前 |
| --- | --- | --- |
| Define Remote | 保持打开 | 关闭 |
| 错误位置 | URL 旁的气球 + 红框 | 管理器 `.remote-notice` 内联 |
| 远端是否写入 | 否 | 是（随后 auto-fetch 失败） |
| 管理器通知 | 无 | `Added badremote. ... 502` |

因此：
- 不能把 Augit 的内联 notice 写成“与参考一致”；
- 也不能把参考气球当作参考独有环境而忽略：它是双方共享的 Define Remote 流程；
- 修复方向应是：失败时不写入/回滚远端，保持 Define Remote 打开，在 URL 处显示错误；
  具体实现需同时覆盖新增与编辑远端，避免只改新增路径。

## 4. 边界

- 只在参考 fixture 上验证新增远端（Name=origin）；未验证编辑远端、删除/重命名、
  认证成功、不同错误类型。
- 参考窗口被用户 ChatGPT 浮层部分遮挡；本批用标题栏点击将其置于前台后截图，
  没有关闭或修改用户 ChatGPT 窗口。
- 这是参考行为/截图证据，不是参考像素配对；页面级仍 50、像素仍 0。
- 参考 PyCharm PID 21940 已关闭；未写远端配置；未修改 fixture 文件。

## 5. 复验注意

- 参考窗口标题栏被其他窗口遮挡时，先点击其可见标题栏，不要按进程名清理用户应用。
- 该路径不依赖 JAB；键盘 `Ctrl+Shift+A` + 截图足以复现。
