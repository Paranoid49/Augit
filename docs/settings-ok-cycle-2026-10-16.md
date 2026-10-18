# 设置OK保存关闭全帧接续（2026-10-16）

## 验证范围

用户最初报告的是设置点OK后长时间卡住。既有独立设置功能回归覆盖保存，但完整开关录像只点Cancel，不能用取消录像代替真实写入路径。本批扩展record-settings-cycle.cjs，增加显式--record-save与--record-dirty：修改隔离实例的终端Shell草稿，切回外观，再点OK；严格检查设置版本只递增一次、回读的Shell与草稿一致。未启动终端，也未修改用户真实配置。

新增--record-only只执行本条完整录像和关闭清理断言，不重复其它八组功能回归。检查点改用已有waitForCheckpoint，在继续或超时时清理本次标记。测试覆盖未写入、双写、错误持久值、Cancel意外写入和前景干扰反例。

开始时检查截图里的“Restore the last workspace and tabs at startup.”：产品规格§7规定普通启动恢复工作区和标签，这里是说明文字，没有可选开关；未自行新增复选框。设置保存及取消边界核对现行UX§7.17和本地IntelliJ SettingsDialog.java的applyAndClose／Apply实现。

## 真实运行结果

证据目录：artifacts/pixel-followup-2026-10-16/settings-ok-dirty-release。使用Release随包web，复制当前用户配置到独立临时文件及profile；没有scene、强制DPI或软件渲染参数。主窗口恢复真实Augit工作区、标签和面板。

- OK关闭耗时66ms；版本0→1，隔离配置terminalShell保存为CommandPrompt。
- 主页面没有重复设置表单，独立设置target在关闭后消失；旧模态深度0、快照0、遮盖0、所有者恢复，前端错误为空。
- 首次原生呈现1741ms：控制器开始19ms、完成145ms、导航319ms、页面准备1444ms、表面渲染1700ms、呈现1741ms。CDP页面就绪观测2779ms包含发现新页面和审计开销，不能等同用户可见耗时。
- 本次再次出现秒级首次打开，不能用之前298–494ms的样本宣布慢开已修好。录像存在采集开销，也不把这单次值当作所有正常启动的固定延迟。
- 录像采用ddagrab桌面复制、FFV1无损、2880×1716、保留静止重复帧；前景采样仅出现本实例主窗24707200和设置19597686，没有其它窗口干扰。
- cycle-screen.mkv SHA256：90c8e5bcff5b30a049d7e796378da780cfc9aa2c2a9adb9923a4c6f2c74c0a0c。

contact-diagnostic.png仅为缩小后的观察索引，不能用于像素计数。全帧原图、录制时间、原生分段耗时和持久化结果分别保留在录像及JSON中。

## 全帧检查结果

analyze-settings-cycle.py --open-seconds 4.5 --whole-state检查全部323帧，每帧4942080像素，没有排除区域；RGB容差2，最大时间戳间隔38ms。参考为第0／247／322帧（0／4.499／5.964秒）。整帧最大差90446像素发生在切换Terminal分类的第210帧，已查看原图，属于真实分类内容变化，不能误报成闪烁；逐像素包络最大63057也发生在该帧。

关闭前第268帧（4.873秒）与已打开状态差40820像素，原图cycle-close-268.png显示Saving settings…和禁用字段，是实际保存进行态。第269帧（4.885秒）起至第322帧，连续54帧与最终关闭状态在RGB容差2内全部相同，没有筛除边缘或阴影。本样本未复现长时间关闭卡住或关闭后残留。截图抽帧首次PowerShell过滤表达式转义失败，已改用Python参数数组按精确帧号提取，不改变原始录像。

以上是本次Light、单次真实脏草稿OK路径；不是所有主题、其它模态或全平台无闪证明。录像最大间隔38ms意味着不能声称捕获了每个显示器刷新周期。首次慢开仍是有效未解决项，且应按导航→准备阶段继续定位，不以本批关闭成功注销。

## 验证与启停

审计修改定向9项通过（175ms）；JavaScript语法、共享9资产和git diff --check通过。产品四文件MD5未变：mockup.js=6A091BAA783027FBE2D040A48F7A737B；mockup.css=D5342D22C245695A57F93BC90A1DC7ED；live-data.js=0F266297202DF61DBEB9D011211FC5FA；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。没有产品代码变更，不重跑全量Shell测试或构建。工作区初始510条状态，155个已跟踪文件变化；混合修改保留，未回滚、暂存或提交。

Augit9680、前景记录器24256、FFmpeg13132以及分析Python23960／解码器9564均已结束；调试端口60621无监听，临时目录Augit-independent-settings-49kthT和本次cycle-continue均不存在。取证文件与原始录像保留，历史被策略拒绝的清理目标未重试。

```powershell
# 普通启动。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 优先正常关窗；异常时只停止启动器打印的本次PID。
taskkill /PID <本次PID> /T /F
# 定向OK录像；确认前景后使用新目录内的cycle-continue检查点，脚本自动关闭自身实例。
node tools/audit/independent-settings.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/settings-ok-manual-new --release --record-cycle --record-save --record-dirty --record-only
```

整体55页／全部模态配对、终端及冲突逐行、其它模态刷新和剩余阴影仍未完成。Windows10按用户决定排除，跨屏DPI未验证。
