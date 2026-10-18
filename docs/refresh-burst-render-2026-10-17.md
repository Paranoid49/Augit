# 连续写入与遮盖渲染确认对照（2026-10-17）

本批取得上一轮串行消费修复的正常入口连续写入证据，同时复现模态窗口整块露出底层正文。新增遮盖自身渲染确认候选没有证明稳定收益，已精确撤销本批新增行和对应契约测试，强制非增量重建恢复。没有回滚既有混合修改，没有新增功能或页面，没有提交。整体未完成。

## 连续变化的证据

`tools/audit/refresh-daily-file.cjs --burst`先修改本批临时文件，观察真实git/status请求在途时再次写同一文件；不扣住桥接应答、不注入产品状态或人为延长产品等待。每轮保存第二次写入时刻、完整请求、最后正文版本及撤盖结果。新增自动化检查拒绝缺少两批、未完成、重叠或未在查询期间写入的样本。

四组共16轮均记录两次git/status无重叠，第二次写入落在首批查询期间，稳定后正文为2/4/6/8最终版本。该证据补齐正常入口下的消费顺序验证，但不是所有状态读取来源的全局排序证明。

## 实机对照与失败边界

所有目录均在`artifacts/pixel-followup-2026-10-17/`，使用tools/start-augit.ps1强制非增量Debug构建、真实配置副本、默认Session，附CDP；DPI168、DPR1.75、界面Microsoft YaHei UI12、实际正文JetBrains Mono13。四组runtime.json均核对实际字体与执行网页，终端未打开。process-identity.json的Modules枚举为空，实际映射以mapped-images.json为准，不能把空枚举称作无注入。

| 目录 | 最后撤盖毫秒 | 帧数/最大间隔毫秒 | 最差变化像素 | 结论 |
| --- | --- | --- | --- | --- |
| refresh-serial-burst | 626.2 / 569.9 / 543.9 / 520.5 | 308 / 61 | 122901 | 第63帧1.051秒目视确认底层正文露出 |
| refresh-cover-render-confirm | 682.3 / 582.1 / 573.6 / 616.3 | 320 / 35 | 187 | 未复现整块正文，边缘仍变 |
| refresh-cover-render-confirm-repeat | 679.9 / 605.6 / 588.9 / 583.3 | 324 / 40 | 265 | 未复现整块正文，完整表面仍失败 |
| refresh-cover-render-restored | 641.5 / 510.6 / 582.1 / 540.2 | 311 / 45 | 187 | 恢复原实现也未复现正文，不能将前两组当作修复证明 |

候选仅在Snapshot.ShowCoverAsync的两帧应答后调用遮盖控制器自身CapturePreviewAsync至Stream.Null，再沿既有DwmFlush保护继续；未更改实时控制器、背景图或DOM。候选增加耗时且没有可靠因果证据，故不保留。不是此前取消首图保护、修改显隐、裁切或边框的重复试验。后续应定位正文露出帧所处的捕获/替换/撤盖阶段，不再无证据添加等待。

全部录像比较585×252完整表面，RGB容差2，未裁边或排除光标，fullPresentationVerified=false；前景监视均无观测到的外部切换，但离散采样不能证明每个显示帧。桌面复制相对时间不能直接套用旧GDI UTC关联器。首组最差原图已目视确认；不能将其仅记录为187边缘像素，也不能用后续未复现覆盖该反例。

四组录像SHA256依次为：

- d434c29fd298756c0746eeb72f4f0a6298cfef9a98869839b36dcece348c477d
- 9f4169f1f97a975a3da6c5e7e9c09ed5b08eb713f11136b8699f4427ca6f4999
- 8be9d52a51822e56b6b4870c4933dc6471fe72e85784866e36448025cfc44433
- 3d83d5df88cb97559d5f0c19643b82dcd0ffabebb9ce4d4330831ceeda7f6fe6

候选Shell DLL SHA256=67D7863720FCC039801E530901EB3DBFEEFBF5D7D15F3BECCB70F586B108A77B；恢复后为94EF57F58A44D101CAECF8E3A8E9AD79B10E0C758E62BCCD42C8105ACF78EF46，与候选前一致。最终ShellModalHost.cs SHA256=9FCCA75D2C87A3AF7B41D79B34547BBA95FE3E6FE600542A1D5CB0DD67E100FD。四组实际live-data.js SHA256=47BA918518C9185AEB2F31E3FED50454879E89228B315B2E702D61FD1ACC1D6D，与当前源文件一致；MD5为3C4854B89FBB9442824847D44976A380。其余网页未改，Release未更新。

## 测试与清理

最终消费、区域刷新、审计及快照脚本29项通过，1.180秒；候选7项脚本/契约测试及C#格式检查通过，不代替真实COM验证。9项共享资源与git diff --check通过，未运行全量测试。保留的可执行修改仅为审计连续写入选项及其回归，上一轮产品串行消费保持。

四批独立cleanup-independent.json确认下列本批资源均已退出/删除：

| 批次 | Augit/前景监视/录屏PID | 端口 | 临时目录后缀 |
| --- | --- | --- | --- |
| 连续写入 | 18792 / 20860 / 22940 | 64878 | JOH2iD |
| 候选 | 28660 / 15836 / 11704 | 56636 | CcryI4 |
| 候选重复 | 1752 / 26464 / 26936 | 50187 | fAh3Hz |
| 恢复 | 7316 / 27208 / 22852 | 60136 | UZW5Te |

临时目录均为系统Temp下Augit-daily-refresh-加对应后缀。正常跳转行关闭及宿主释放均完成。历史参考清理被自动审批拒绝和Nahimic服务权限阻塞仍保留，不宣称全局零残留或干净环境性能验收。设置慢开、无CDP完整开关、500毫秒、透明帧、全页面与阴影仍未完成；复杂Git及冲突像素精修按用户方向暂缓。

## 复验命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 优先正常关窗；异常只终止本次核实的实例。
taskkill /PID <本次PID> /T /F
node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新证据目录> --git-fixture --burst
D:/Python/Python3119/python.exe -B tools/audit/analyze-static-modal-video.py artifacts/pixel-followup-2026-10-17/<新证据目录>
```
