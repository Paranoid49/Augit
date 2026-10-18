# 局部刷新意外关闭弹窗的修复（2026-10-17）

本批修复正常入口可复现的问题：提交侧栏存在Changes与Unversioned Files两组时，外部新增空目录会触发局部侧栏更新，却意外整页重建并关闭正在输入的跳转行弹窗。修前实机失败，修后窗口、正文节点、输入框、焦点和输入内容均保留；正常取消与资源释放通过。该局部修复不代表背景透明帧、500毫秒或设置慢开已通过。

## 根因与实现

`refreshStatusRegions()`此前无条件尝试patchChangesList。成功时从目标区域中移除side，再调用refresh；当唯一目标是side时，剩余参数为空，而refresh()的既有语义是整页重绘。整页重绘移除原dialog，原生同步随后发出window/modal-close，输入和焦点一同丢失。

现在仅当请求包含side时尝试改动列表补丁，补丁成功且仍有其他目标才调用refresh。侧栏原地更新仍执行restoreChangesState与syncChangesToolbar；补丁形态不符继续按原目标替换。没有改变无参数refresh的全局语义，没有新增功能或页面。

修改：`web/src/live-data.js`；新增真实函数回归`tools/audit/status-region-scope.test.cjs`；正常入口驱动`tools/audit/refresh-daily-file.cjs`增加`--scope-regression`。两个新回归修前失败：侧栏单目标触发空参数refresh，非侧栏目标也触碰侧栏。修后四项范围回归通过，连同11项区域刷新和5项消费测试共20项通过。

## 正常入口修前与修后

三批均从tools/start-augit.ps1强制非增量Debug构建启动，复制真实配置、默认Session、正常DPI，附CDP。只在本批临时Git夹具中创建文件与空目录，不操作用户仓库。通过真实界面打开文件、切换提交侧栏和Ctrl+G；只保存DOM引用用于比较，不注入产品状态或调用内部刷新。

| 证据目录（artifacts/pixel-followup-2026-10-17下） | 实际结果 |
| --- | --- |
| refresh-scope-before | 根节点、弹窗、输入框和焦点全部失去；原生窗口25954756变0，记录modal-close；断言失败 |
| refresh-scope-after | 四项身份全部保留，同一HWND；正常Esc关闭成功 |
| refresh-scope-draft | 原输入框中的17保留、焦点不变，HWND5769566保持；正常Esc关闭后快照和遮盖均释放 |

最终批次仍真实读取workspace/changes、git/status、workspace/list；随后仍有modal-cover/refresh/uncover，未吞掉通知。关闭后active=false、snapshotCount=0、captureCoverWindow=0、ownerEnabled=true，网页错误数组为空。存在的背景截图成本仍需优化，本批不宣称无闪或性能改善。无新增系统录像，不用DOM身份检查替代屏幕像素验收。

实际DPR1.75、界面Microsoft YaHei UI12、正文13；实际正文节点加载随包JetBrains Mono。最终执行live-data.js SHA256=`C70B9D41BE6B84A2D1EDDC241AA4BAB7585557E381659A44CE7B95F739DE8807`，MD5=`458DFB81C5F38777AFF4C91DA6380A81`。Shell DLL保持`94EF57F58A44D101CAECF8E3A8E9AD79B10E0C758E62BCCD42C8105ACF78EF46`。完整执行身份、映射、配置与字体见每批runtime.json、mapped-images.json及launch.json，不能仅用磁盘文件代替实际执行身份。Release未更新。

## 清理与未完成项

三批cleanup-independent.json确认PID17692/1884/19824已退出，端口50473/49724/58208无监听，Temp下Augit-daily-refresh-iH2OjU、NZUo0o、hcP9l0目录已删除。此次没有启动录屏服务器。历史参考清理自动审批阻塞和Nahimic权限阻塞仍保留，不宣称全局零残留。既有混合修改没有覆盖、回滚、暂存或提交。

设置慢开、无CDP完整开关、背景透明帧与500毫秒、全页面和阴影继续未完成；冲突与复杂Git界面精修按用户最新方向暂缓。后续可检查同值列表补丁是否仍产生多余DOM变更，但未取得相应实测前不能宣称截图减少。

## 启停与复验

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 优先正常关闭；异常只终止本次已核实实例。
taskkill /PID <本次PID> /T /F
node --test tools/audit/status-region-scope.test.cjs tools/audit/workspace-change-render.test.cjs tools/audit/workspace-change-delivery.test.cjs
node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新证据目录> --git-fixture --scope-regression
```
