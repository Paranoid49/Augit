# 独立设置阴影采集接续（2026-10-16）

## 本批结论

独立设置已经进入正式路径，但旧capture-shadow-shell.cjs仍等待主WebView重挂信号、在主页面定位设置表单，并要求存在背景快照。本批修正审计入口，不修改产品视觉或原生生命周期。旧架构通过记录不能替代独立设置验收。

新增shadow-settings-surface.cjs直接读取window/settings-state，等待独立CDP页面出现，严格要求恰有一个设置页面、主页面无设置表单、主控制器未重挂、快照与遮盖为零、所有者在设置呈现时禁用且关闭后恢复。重复入口必须保持同一设置句柄。发现CDP目标公布晚于宿主ready，新增有界等待；等待不接受重复页面。

正常入口仍与tools/start-augit.ps1一样使用源web目录、不带scene，独立配置及profile仅隔离审计；新增显式--bundled-web用于另验随包入口。本次未使用此参数，不能写成随包采集。Debug和Release各24份网页镜像另以哈希核对一致。

旧设置截图、刷新遮盖和DOM定位参数在独立设置路径直接拒绝，跳转行保留原有路径。--reference-frame对独立设置只校验真实外框，不用CSS伪装原生窗口尺寸。指针128物理像素安全距离现同时适用于设置。

## 原始证据与比较边界

成功证据目录：artifacts/pixel-followup-2026-10-16/shadow-independent-settings-clean-pointer。Windows11、Light活动态、DPI168、1.75倍；设置默认原生外框1820×1330，采样区域2076×1586，四侧128物理像素。截图前后前景身份、窗口位置、DPI和指针位置保持一致；原始PNG已目视检查。

- settings.png SHA256：623fe7a900a3f8bf50d23ee2284e9fe3119402d45519c9fd968f453f13bd9aa1。
- background.png与background-repeat.png均为a059f5b51233ca8b47beaf301717476cfb0119f37e4ff54f583d15808a8df4bc，关闭后背景稳定。
- 唯一设置句柄44499202；主页面无重复表单，关闭后快照和遮盖均为零，所有者恢复。
- 对照PyCharm2026.2.1既有shadow-body-reference-light的64物理像素外角：每角12288像素，下方两角同背景且最大RGB差2；上方各384背景像素不同，严格判未通过。corners-64.json退出码1，保留失败结论，不筛除不同背景像素。

参考外框1555×1215与本次默认设置尺寸不同，完整外环及内角工具要求同尺寸，所以不执行无效整窗比较，也不缩放PNG。局部外角比较只按各角整数位置对齐，不代表完整阴影一致。当前截图不是录屏，不能用于宣布全开关过程零闪烁；仍需同尺寸、完整同背景参考及新版本全帧录像。

## 失败采样与修正

本批共六次隔离启动，失败不计通过：

1. shadow-independent-settings-light：宿主ready后CDP目标尚未公布，新增有界等待。
2. shadow-independent-settings-light-native：桌面工具激活了所有者，前景不是设置子窗，门禁拒绝。
3. shadow-independent-settings-native-final：Edge在前景，门禁拒绝。
4. shadow-independent-settings-native-active：真实设置前景已成立，但指针光晕可能进入采样带，拒绝。
5. shadow-independent-settings-pointer-clear：指针仍未离开完整安全带；一次桌面状态刷新参数错误已纠正，不计采集成功。
6. shadow-independent-settings-clean-pointer：指针移到左上采样区外，沿产品重复入口激活同一设置窗口后采集成功。

前景和指针门禁未放宽。采集脚本补充独占写入failure.json，后续失败可追溯。没有据上述审计问题调整产品阴影或字体。

## 验证与资源

新增4项设置状态测试及既有4项几何测试共8项通过（151ms）；JS语法、共享9资产、Debug／Release各24网页镜像、git diff --check通过。没有产品逻辑改动，不重复全量Shell测试或构建。分支dsh、HEAD3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；混合修改保留，未暂存、提交或回滚。

产品四文件MD5未变：mockup.js=6A091BAA783027FBE2D040A48F7A737B；mockup.css=D5342D22C245695A57F93BC90A1DC7ED；live-data.js=0F266297202DF61DBEB9D011211FC5FA；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

六次PID27404／20156／19128／27320／27852／300均结束；端口62703／60835／56490／63523／58618／64486均无监听；临时目录Augit-shadow-pair-aWk1EH／rIcvzV／703JNK／BTVWTK／ULOQk4／XelWU2均已删除并独立核验。没有启动参考PyCharm。历史被策略拒绝的目录与标记未重试。

## 启停与后续

```powershell
# 日常入口，不需要审计参数。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 优先正常关闭；异常时只停止启动器打印的本次PID。
taskkill /PID <本次PID> /T /F
# 采集工具自动清理自身实例；检查点需确认真实前景和指针位置。
node tools/audit/capture-shadow-shell.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/settings-shadow-manual-new Light --normal-entry --uniform-background --shadow-radius 128 --position-window
```

整体整改仍未完成。55页及全部模态配对、终端／冲突逐行、其它模态刷新和剩余系统阴影继续保留；Windows10按用户决定排除，跨屏DPI未验证。
