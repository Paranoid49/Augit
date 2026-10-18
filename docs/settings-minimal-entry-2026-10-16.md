# 独立设置入口资源精简（2026-10-16）

## 修改及依据

上一批真实OK录像仍记录首次原生呈现1741ms，不能据正常样本否认偶发慢开。本批从当前工作区和导航资源事实开始检查：独立设置仍导航index.html，额外加载xterm.js、xterm.css、addon-fit.js、current-find.js和image-preview.js，尽管设置没有终端、文件查找或图片查看器。

新增web/settings-surface.html，仅引用共享mockup.css及live-data.js，并在模块执行前落实主题；Shell依据是否为独立设置选择入口，普通主窗仍为index.html。设置表单、草稿、桥接、字体度量、保存和原生呈现顺序复用原实现，不复制DOM、不改变外观令牌、不缓存控制器、不新增依赖。live-data.js仍有共享功能模块静态导入，本批没有将整个设置实现拆开，不能宣称只加载设置代码。

独立设置不再请求上述五项，共542521字节本地源文件；这是文件大小之和，不是网络传输量或内存收益。资源计时实际记录17项→12项；WebView虚拟主机的transferSize／decodedBodySize返回0，不能用这些字段推断字节收益。普通主窗仍存在Terminal构造器，设置页没有该构造器，真实回归同时断言两侧。

## 测量

前后均为Release、复制真实用户配置到隔离文件及profile、普通Augit工作区、无scene或强制DPI／软件渲染。每组3次打开，包含首次冷profile和后续重开；没有同时录像，故不得直接与上一批录像中的1741ms作因果比较。

| 指标 | 修改前 | 修改后 |
| --- | --- | --- |
| 原生呈现ms | 320、291、446 | 314、289、338 |
| 导航到页面准备ms | 137、102、173 | 117、102、132 |
| 导航资源项数 | 17、17、17 | 12、12、12 |

证据分别在artifacts/pixel-followup-2026-10-16/settings-resources-before及settings-resources-after。审计补充每项资源、导航事件和桥接应答耗时。初读配置本批约1–22ms，字体枚举约19–29ms；样本没有复现秒级阻塞，未确定偶发慢开的根因。仅能确认移除了不必要资源，不能称性能问题彻底解决。

## 功能与产物验证

- Release随包settings-minimal-entry-release八组通过：Cancel／OK／Escape／标题关闭、主题取消、真实持久化失败重试、迟到保存取消后重开、Apply后重开。前端错误0；10次原生呈现314–471ms，关闭43／127／74／67ms。
- Debug日常源网页settings-minimal-entry-debug三次启动通过；设置加载精简入口、字体就绪、唯一页面，主窗口保留终端库。
- JS入口及隐藏启动7项通过；Shell IndependentSettingsTests 17项通过（75ms），新增两条入口选择用例。新增JS测试首次正则转义有误，改为明确脚本分隔读取后通过，不将失败计作通过。
- Debug／Release强制构建均0警告0错误，产物各25文件镜像与web一致；共享9资产一致、C#格式、JS语法、UTF-8／LF、git diff --check通过。未改变公共桥接契约或生命周期，不重复全量测试。
- first-layout-cdp.png及first-layout.json已目视：共享表单完整、DPI1.75、唯一设置表单。仅为CDP布局诊断，不替代系统原始截图、阴影或新版本开关录像。

产品四网页MD5未变：mockup.js=6A091BAA783027FBE2D040A48F7A737B；mockup.css=D5342D22C245695A57F93BC90A1DC7ED；live-data.js=0F266297202DF61DBEB9D011211FC5FA；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。新增入口及Shell导航已编译到两套产物。保留混合工作区，未回滚、暂存或提交。

本批四次隔离实例7708／24832／26772／15212及对应profile子进程均退出；63420／52367／63068／53753端口无监听，Augit-independent-settings-9pzYoI／2lg6FL／Htj1z4／nNbTfo临时目录均不存在。没有启动参考程序或录屏服务器；正式JSON和布局证据保留，历史被拒绝的清理目标未重试。

## 启停及边界

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 优先正常关窗；异常时仅停止启动器打印的本次实例。
taskkill /PID <本次PID> /T /F
# 定向启动资源审计，自动结束隔离实例。
node tools/audit/independent-settings.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/settings-entry-manual-new --release --startup-only --expect-minimal-settings
```

55页和全部模态同态、终端／冲突逐行、其它模态背景刷新及完整阴影仍未完成；本批也没有注销偶发慢开。Windows10按用户裁决排除，跨屏DPI保持未验证。
