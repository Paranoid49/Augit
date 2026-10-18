# 真实正文背景外沿配对（2026-10-15）

## 本批结论

上一轮取得新参考背景并否定遮盖重定向试验，本轮完成浅色活动态同尺寸窗口四周64物理像素的完整外沿配对。370944像素全部同背景，八区均在RGB容差3以内，顶部和底部最大通道差1，其余六区为0。未修改产品阴影参数。

但64像素不是完整阴影范围：两端最外层各6048像素中3953仍不同于背景，最大通道差26。不能因为两端相同便忽略采样截断；fullShadowVerified仍false。下一组须同时减小两端取证窗口高度，再取得完整128像素外沿，并再次检查远端是否归零。圆角矩形内侧、深色和非活动态也仍待完成。

## 采集条件与失败修正

- 新增shadow-capture-placement.cjs及三个测试：从真实code-view边界和参考DWM尺寸计算整物理像素位置，为四周采样带留空间；空间不足直接拒绝，不裁图、不缩放原图。capture-shadow-shell增加--place-inside-body与--shadow-radius，仅在创建窗口前注入取证位置/宽高，不改颜色、阴影或正文，不作为默认布局验收。
- 普通入口恢复配置原用斜杠路径，ShellBridge直接与反斜杠工作区路径比较，导致设置返回openFiles空数组；本批现场记录tabs为空、无前端错误，见shadow-body-ring-light-diagnostic/restore-failure.json。refresh-capture-startup改用path.resolve保存规范路径，恢复后必须等待真实Ag 0123456789正文出现再点击工具区。
- 此发现限制此前refresh-fast-cover及其他采用相同原始normal-entry配置的证据：普通入口/主题/字体声明仍成立，但不能继续声称它们已恢复notes.txt。历史刷新失败仍有效，不能撤销；成功延迟只限当时实际背景。本批不修改日常用户配置或产品会话比较逻辑。
- 工具区首击可能只激活，第二击才收起；按实际layout状态最多点击两次并等待两帧。本次真实正文823.4286逻辑px高，1555×1215物理px窗口只能容纳64px四侧带，128px被空间断言拒绝。
- 前五次采集分别因恢复未出现或面板空间不足结束，不计通过，全部进入finally清理。第六次完成settings及两份背景，哈希相同，无前端错误，关闭释放断言通过。

参考原先所谓“鼠标光晕在矩形内部”只描述指针中心，不足以证明整个光晕在内部：本批与旧参考比较顶部仍有10226背景差异像素。因此重新启动隔离参考，移动Settings并将指针移至窗口正文中央，再采窗口和关闭后的同位置背景。新原图确认外沿无光晕、分隔线或通知遮挡。此前pixel-refresh-redirection-shadow记录的外沿无光晕判断由本段更正。

## 权威证据

两端DPI168、scale1.75、Light活动态、DWM尺寸1555×1215。原始PNG来自System.Drawing.CopyFromScreen，无重采样，比较仅按DWM左上角整数偏移。

- 实际：artifacts/pixel-followup-2026-10-15/shadow-body-ring-light-final，settings SHA256为61b33c57cd6c03d35b6151bbf26ce576ab8bf3604e51f8cfec5e877af6ccb398；background及repeat为a6ae1501eee50c545da1411e513e98f59b61377e86a7cca3fe1a5a23f044e513。
- 参考：artifacts/pixel-followup-2026-10-15/shadow-body-reference-light，settings为63b4da7285e6ead681b13361fb51ff4f5e9c77b287151a96f703e5652d101254；background及repeat为0cf0f5dae7ed60d2873c1a9571f2dfb2a0f3b39892f703c548ed2e63ba790bd6。
- ring.json为与旧参考比较的失败结果；ring-clean-reference.json为新参考64px完整外沿结果；outer-boundary.json明确记录未归零的远端。三者均保留，不覆盖或筛除失败像素。

## 验证、资源与边界

六项采集配置/位置测试通过，采集脚本JS语法检查、共享资源检查、git diff --check通过。没有产品可执行逻辑变更，无需重建或全量测试。分支dsh、HEAD3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；开始时419条工作区状态，154已跟踪文件变化，保留混合改动，未暂存或提交。

产品四网页MD5未变：mockup.js=6E61081C842CA96770B3F8BB56E13B0A；mockup.css=99A3D1E61C7D12BA82DD25CE2E685A76；live-data.js=641B29F3BC50EC22500924F64B9C63CE；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

本批Augit25372/24312/23928/7092/24972/14964均退出；临时目录aGQ4Pu/oMhkoR/wkBTEU/jqm30g/SJzECW/148SD1均删除并独立核对。参考25096及子树23560/8144/25804/25440/18840/11588/21252已停止，启动环境变量已恢复。正式证据保留，历史被拒绝清理的目录未重试。

## 复验与停止

```powershell
$pw = 'C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node --test tools/audit/refresh-capture-startup.test.cjs tools/audit/shadow-capture-placement.test.cjs
node tools/audit/capture-shadow-shell.cjs $pw artifacts/shadow-body-manual-new Light --normal-entry --uniform-background --reference-frame artifacts/pixel-followup-2026-10-15/shadow-body-reference-light/settings.png.json --place-inside-body --shadow-radius 64
# 驱动正常由finally停止；异常仅停止本次输出PID。
taskkill /PID <本次输出PID> /T /F
# 日常入口
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
```

55页/全部模态双端同态、背景刷新透明帧与完整延迟分布、终端/冲突逐行及完整系统阴影仍未全部完成；Windows10按用户裁决排除，跨屏DPI保留未验证。
