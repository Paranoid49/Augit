# 设置日常启动器验收（2026-10-16）

本记录为修前反例及启动器验收；后续呈现顺序整改见[激活后渲染记录](settings-activation-render-2026-10-16.md)，原始失败证据保留。

用户要求普通启动与测试一致。本批将独立设置回归接入实际 tools/start-augit.ps1；此前直接运行Debug/Release EXE的独立设置结果不能替代这个入口的验收。产品独立WebView及精简入口沿用上一批实现，本批没有改产品视觉、窗口逻辑或用户配置。

## 入口与验证范围

independent-settings.cjs 新增 --daily-launcher，复用已有 daily-launch-command.cjs。启动器执行强制非增量Debug构建、加载源码web，并沿用默认WebView Session目录。不传场景、DPI、窗口尺寸、字体或GPU覆盖。仅设置文件使用用户当前配置副本，保存及失败重试不修改用户原配置；调试端口用于读取真实页面。新增模式拒绝同时使用 --release、--source-web 和 --scene-settings，避免报告与真实入口不一致。

证据目录 artifacts/pixel-followup-2026-10-16/settings-actual-launcher：

- 八组回归通过，网页错误0。取消、OK、Esc、标题关闭分别52、38、58、66ms；主题预览取消、真实持久化失败重试、迟到保存后重开、Apply后重开通过。
- 十次创建至原生呈现296、303、477、418、416、413、405、434、435、447ms。每次创建期间并发八次请求、呈现后再次请求，都复用同一非零HWND；设置页面唯一，主页面不含设置表单。
- 不产生主界面重挂、设置背景截图或刷新遮盖；主窗继续使用原控制器。以上为功能和资源路径验证，不等于全部Present帧无闪。
- launch.json保留真实启动命令、构建输出及默认profile身份。Debug强制构建0警告0错误，25项随包资源与源码一致、9共享资产一致。

新增启动分支自动化测试包含有效PID、缺失PID拒绝、禁止绕过启动器及报告字段。首次测试夹具遗漏port变量导致失败，补齐后四项全部通过；JS语法和git diff --check通过。本批未修改公共产品契约，不重复全量Shell测试。

## 真实开关录像

证据目录 artifacts/pixel-followup-2026-10-16/settings-actual-launcher-cycle。使用同一日常启动器及默认profile，电脑操作技能观察并激活真实主窗后取得DDA原始录像。实际编辑隔离终端Shell草稿、返回外观、触发背景文字更新并点击OK，版本0→1且持久值匹配。OK关闭55ms；首次创建至原生呈现1228ms，慢开仍未解决。记录中一次背景更新间隔约3秒，未确证原因，不把采集或调试延迟归因成产品主线程卡顿。

全部420帧、2880×1716原始画面检查完成，排除0像素、RGB容差2，最大采样间隔51ms。第167／168帧（3.035／3.045秒）出现整块白色设置客户区；cycle-worst-whole-state.png已目视确认。最大整帧最近状态差995646像素，颜色包络越界534247像素。白帧之前已有设置表单，之后为Terminal分类，不能仅凭未校准的墙钟事件把原因认定为首次揭示；需继续区分原生合成与分类重绘。此次是明确失败证据，不能用八组功能通过或历史快样本覆盖。

第370–419帧（6.870–7.904秒）全部与最终关闭状态一致，共50帧；关闭前一帧差异40820像素，包含保存状态，未冒充零差异。录像SHA256为715de926d82c5109ff900c734f430561e1db1138ebdcb328f043b08e1a4839f8。录屏前景126个采样均属于本实例，不代表采样间所有Present事件均已观测。

本批两个Augit实例16180／21244、录屏5084、前景监测22788、分析Python8592及解码ffmpeg22236均已结束；53706／57910无监听。两份本批临时配置目录h5GEjC／QhaWVa及检查点已清理。默认Session是用户既有缓存，按原样保留；没有重试历史被拒绝的清理目标。共享产品四文件MD5保持6A091BAA783027FBE2D040A48F7A737B／D5342D22C245695A57F93BC90A1DC7ED／0F266297202DF61DBEB9D011211FC5FA／F297BF8769E249CA0D804F4F4EEEF5A9。

## 启停与边界

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 优先正常关窗；异常时仅停止启动器打印的本次实例。
taskkill /PID <本次PID> /T /F
# 以下为定向验收；驱动finally自动停止其启动的实例。
node tools/audit/independent-settings.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/settings-daily-manual-new --daily-launcher --expect-minimal-settings
```

55页及全部模态同态、终端和冲突逐行、完整阴影、偶发慢开及全部背景帧尚未全部验收。Windows10按用户要求排除，跨屏DPI保持未验证。没有暂存、提交或回滚混合工作区。
