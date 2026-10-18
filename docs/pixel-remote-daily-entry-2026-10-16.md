# 远端日常入口与设置复验（2026-10-16）

本批接续独立设置WebView及远端取证，保留已有混合修改，没有暂存、提交或回滚。分支dsh，HEAD为3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；开始时工作区521项变化，git diff --check通过。Windows10按用户裁决排除，跨屏DPI保留未验证。

## 正常入口复验

独立设置已在此前批次实施，本批未再次重写其架构。实际执行tools/start-augit.ps1、强制非增量Debug构建、默认WebView Session及当前配置副本；不指定场景、DPI、字号或GPU参数。八组回归通过，创建期八次并发请求及呈现后重复请求保持同一HWND；主网页没有设置表单，设置页没有工作区副本；取消、OK、Esc、标题关闭分别92/52/70/94ms，网页错误0。主题取消、真实写入失败重试、迟到保存和Apply重开通过。

十次原生呈现为616/2944/2968/2876/537/523/567/2651/546/517ms。慢样本并不都停在同一阶段：有控制器创建慢，也有准备或渲染阶段慢，尚未定位共同原因。本次没有新设置录屏，不能据此宣布零闪或性能通过。证据：artifacts/pixel-followup-2026-10-16/settings-resume-check。

## 远端取证入口纠偏

上一版capture-remote-pair.cjs直接启动EXE，指定1400×930、临时profile和硬编码外观。这不足以代表用户正常入口。本批改用daily-launch-command.cjs调用正常启动器，后者新增可选工作区参数；默认仍是本仓库。取证仅复制当前配置、选择同一参考仓库并启用只读调试端口，不再覆盖窗口大小、外观或profile。

actual-daily-1取得空远端与新增表单原始屏幕PNG，DPI168，前后窗口、进程、前景和边界稳定，指针清除96物理像素。空远端634×410物理px，新增708×459物理px；参考分别624×385及710×363，仍有明显差异。Augit有产品额外Push URL行，不能直接把两行参考高度套入三行产品。两图为差异证据，不计为完整像素匹配。取消Define后精确回到Add按钮，关闭Remotes后仍保留Push，最终快照0、所有者启用、Git配置字节不变、网页错误0。旧断言要求回到表格不符合从Add进入的焦点约定，未因此修改产品焦点逻辑。

## 产品修复

默认新增表单存在2逻辑px正文溢出；字号20时溢出5px。新增测试先复现失败。原因是默认高度公式没有计入border-box上下边框，大字号时也没有按实际复选框画布计算内容高度。现在先应用字段令牌，再量完整表单及正文内距、上下边框，保持原字段顺序、字号、窗口宽度和底栏；受视口限制仍只滚动正文。共享mockup.js两侧同步，未改CSS、live-data.js或bridge.js。

actual-daily-2修后实际正文溢出0，高264.2857逻辑px；该次移窗诊断未成功且改变前景，新增表单屏幕采集被门禁拒绝。actual-daily-3因鼠标光晕位于窗口内也被门禁拒绝。两次均保留已有状态文件，不作为成功截图。电脑操作返回的关联窗口截图会叠加上层内容，不能单凭父窗裁图认定背景快照污染；未能取得独立快照源图，不修改模态机制。

actual-daily-behavior使用同一正常启动器完成修后完整嵌套流程，新增表单溢出0、取消焦点归还Add、关闭后快照0、所有者启用、Git配置字节不变、网页错误0。报告明确screenCaptured=false和pixelRestorationVerified=false。行为模式不绕过截图门禁，也不生成伪造屏幕证据。

## 验证与哈希

- 新增表单布局测试：浅/深色×12/13/20/40字号共8组及各自受限视口通过，浏览器finally关闭；只证明布局与限高。
- 远端受影响模块live-shell --goal-remote-only共41项通过；未重复全仓测试。
- 日常启动器5项、取证模式2项通过；JS语法、共享9资产、git diff --check通过。
- Debug日常入口和Release强制重建均0警告0错误，两套产物各25项web资源镜像一致。
- mockup.js MD5由6A091BAA783027FBE2D040A48F7A737B变为2ACDC2382AEA5A93C0E948B78BA33731。
- mockup.css / live-data.js / bridge.js MD5保持D5342D22C245695A57F93BC90A1DC7ED / 0F266297202DF61DBEB9D011211FC5FA / F297BF8769E249CA0D804F4F4EEEF5A9。

## 启停与清理

日常启动：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 优先正常关闭窗口；异常时仅停止启动器打印的本次PID。
taskkill /PID <本次PID> /T /F
```

定向复验（驱动finally结束自身实例、清理自身临时配置）：

```powershell
node tools/audit/capture-remote-pair.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/remote-daily-manual-new --behavior-only
$env:AUGIT_PLAYWRIGHT_MODULE='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node --test tools/audit/remote-define-layout.test.cjs tools/audit/daily-launch-command.test.cjs tools/audit/capture-remote-pair.test.cjs
```

本批实例23028/26532/11564/18428/26676的finally均已执行。临时目录后缀KXMmvU/hut8p0/hSrwoH/QMKxQH/VRlF5s均已删除；默认Session保留。最后以进程和端口检查复核清理。总体55页、所有模态、终端与冲突逐行、完整阴影和慢开/全部闪帧仍未完成。

清理复核：五个Augit PID及其直接子进程不存在，61365/53504/58225/60824/59877均无监听。上轮参考8416及其fsnotifier、cef_server子树已按路径核实后停止；未停止用户其它应用。
