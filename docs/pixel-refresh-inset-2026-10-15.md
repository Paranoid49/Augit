# 遮盖子窗内缩与重复取证反证

日期2026-10-15。整体目标未完成，Windows10排除本次验收，跨屏DPI未验证。

## 假设与结论

依据四边1物理px DWM扩展区与子窗边缘共同参与合成的假设，试验将遮盖子窗位置改为(1,1)，宽高各减2，源图偏移同步加1物理px再换算逻辑坐标。原图不缩放，父窗DWM边框和阴影、透明底色、首图定尺、两帧显示确认及DwmFlush保持。完整表面仍全部验收，没有排除未覆盖的外沿。

首次浅色、深色样本都没有直边闪亮，但将方案整理为带极小尺寸保护的实现后再次采集，浅色仍出现一次3874像素边缘闪亮。因此前两次未复现不足以证明稳定修复，方案与新增专用几何测试均已撤销。未以单份通过区域的结果替代完整验收。

## 三份证据

根目录：artifacts/pixel-followup-2026-10-15/refresh-cover-inset。所有录像逐帧检查1820×1341完整表面、2440620像素，RGB容差2、排除0像素，首次均明确零字节/零子窗。

| 目录 | 帧数/变化帧 | 最大变化 | 首次ms |
| --- | --- | --- | --- |
| light | 451/44 | 12角部像素，最大RGB6；直边0 | 533.09 |
| dark | 447/47 | 10角部像素，最大RGB5；直边0 | 531.51 |
| final-light | 460/46 | 3874像素：132角部、3742直边，最大RGB134 | 499.47 |

三组内部变化均0，均无重复时间戳，最大采样间隙依次50/34/50ms；严格门禁全部退出1。final-light仅第47帧出现直边变化，不能把前两组没有这类帧解释为因果性改善。最终一次首次499.47ms也不代表稳定达到延迟目标，且同时有格式检查进程负载。

录像SHA256：light为895b94368d5ca56cbbf3be9e27ec90b376740415fd1a7dfbc0dbb43a04019b30；dark为0823df390f572eac684c0e447422bbdf5a1c940f0da3c1934d33ea8cd4b644b7；final-light为a567d6442816f1de9aa691805565fc015eabb0bbd976d73a17c6edc3113c660d。原始FFV1、PNG与元数据、时序、分区分析均保留。

final-light/shadow-control.json将四边中央半段64px阴影暗化中位数与此前原实现refresh-native-messages/light比较，四边最大差均0。此项仅证明该采样剖面未退化，不能替代PyCharm完整阴影验收。

## 定向测试与撤销

整理版实现共20项ShellModalGeometry/ShellSnapshot定向测试通过，84ms；新增两项覆盖六个比例固定物理内缩、极小尺寸与无效尺寸。真实单组生命周期通过（9.16秒），包含关闭竞态、重新打开、嵌套、草稿焦点、快照原图尺寸和释放，证据lifecycle/geometry.json。限定三个C#文件格式检查通过。此类测试只证明行为与几何约束，不证明像素无闪。

最终反证出现后移除本批ShellModalHost、ShellModalGeometry及ShellModalGeometryTests中的内缩实现与专用测试，其余已有修改保留。ShellModalHost逐字节恢复并核验SHA256为77B43F8E4C009DFFB3C15288B2D88CEC8BCB5537D137557893C138841E330B9A；恢复构建0警告0错误，2.56秒。没有保留新增产品逻辑、依赖或规范变更，未执行全量套件、暂存或提交。

## 资源与复验

本批Augit PID20376/24976/13948/4680、录屏PID22940/13608/4824均退出；端口52285/58105/56220/51365无监听；临时Augit-shadow-pair-AZOGDa/eDOkxv/5dEUxI、Augit-pixel-fix-2BufkA均不存在，独立检查通过。恢复备份已删除，正式证据保留；历史被拒绝清理未重试。

当前正式实现复验（不含已撤销内缩）：

    node tools/audit/capture-shadow-shell.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/refresh-inset-followup Light --refresh-video
    python -B tools/audit/verify-refresh-surface.py artifacts/refresh-inset-followup --require-stable
    # 正常由finally清理；异常只停止本次日志记录的PID。
    taskkill /PID <本次Augit_PID> /T /F
    taskkill /PID <本次recorderPid> /T /F

下一步不再仅凭某一次没有直边帧来挑选实现；应取得完整合成提交或可重复的帧状态证据，再判断边框亮变的来源。55页/全模态、终端/冲突逐行和完整系统阴影仍未完成。
