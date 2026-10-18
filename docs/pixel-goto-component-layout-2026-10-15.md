# 跳转行组件布局整改（2026-10-15）

本批接续用户“正常启动全部向测试对齐”的要求，修改两条入口共用的产品资源；没有只在审计页面注入样式。依据上一批 PyCharm 2026.2.1 真实组件数据与已授权布局裁决，修正跳转行输入命中区、内部边框及表单和底栏间距。整体整改仍未完成。

## 实现与边界

输入命中区默认200×34逻辑像素，普通描边内缩3px，聚焦内缩2px；焦点切换不改变布局。新增局部装饰容器，正常入口与静态模板一致；不改其他紧凑输入窗口。外框改为叠绘，表单标签最小93px、间距8px，按钮上下8px。仅支持行号，不新增列跳转。设计规范同步。标题图标、文案及部分像素仍有差别，不宣称整窗复原。

真实取证驱动移除了源码web-root覆盖和此前的scene参数，直接加载Debug exe随包web，从普通入口打开夹具并按Ctrl+G。仍使用隔离配置/profile、固定测试工作区和窗口尺寸，不能冒充用户默认配置全量巡检；此前默认入口32状态巡检见startup-alignment-2026-10-15.md。

## 验证与原图

- 浅深主题×UI12/40×DPR1/1.75/2共12组通过，新增输入200×34、相对位置、焦点切换几何不变、普通/聚焦边框断言。
- 真实外壳11项行为通过，空输入焦点、Tab正反循环、非法输入保持窗口、有效跳转、取消和关闭后快照释放均正常。系统原图584×252，DPI168，已目视检查。
- 首次真实断言因WebView2设备像素取整返回1.71429px而非2px失败；修正断言为误差小于一个设备像素后通过。没有改产品宽度来绕过失败。
- 同一原图ROI[190,85,562,160]、RGB容差2、排除0像素：差异3908→1368，绝对通道差1112940→310343。精确品牌蓝像素边界参考[204,95,545,146]、实际[201,95,542,146]，仍有3物理像素横向差。光标时相未配准；不增加55页合格数。
- 证据：artifacts/pixel-followup-2026-10-15/goto-component-layout/actual-empty.png、actual-state.json、comparison.json。参考原图继续使用goto-current-pair/reference-empty.png。
- Debug/Release构建2.62/1.67秒，均0警告0错误；两套web各24文件与源目录逐字节一致；共享9资产、四项JS语法及git diff --check通过。局部改动未重复全量测试。

## 哈希与清理

mockup.js：612E9976704343ABE1BB8778B077508F → 6E61081C842CA96770B3F8BB56E13B0A。
mockup.css：D8946B4C1BD29A2DBF5E9190D5D36350 → 99A3D1E61C7D12BA82DD25CE2E685A76。
live-data.js：8A39A72F1D0F8FC7D53D3F158205B2C7 → 641B29F3BC50EC22500924F64B9C63CE。
bridge.js：F297BF8769E249CA0D804F4F4EEEF5A9（不变）。

PID10468/7116及其子进程已退出，端口61195/57129无监听；本批两个Augit-goto-pair临时目录均删除。进程扫描仅命中检查器自身。上一批已记录的自动审批拒绝删除目录未再次尝试删除，仍按原记录保留；不能宣称历史临时文件全部清理。未暂存、提交或回滚混合修改。

## 启动与停止

日常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。
正常关闭窗口；异常时仅对启动器打印的本实例PID执行taskkill /PID <PID> /T /F。
定向复验：node tools/audit/verify-goto-layout.cjs <Playwright模块路径> <Edge路径>。
真实取证：node tools/audit/capture-goto-line-shell.cjs <Playwright模块路径> <新输出目录>；finally自动停止本次进程并删除临时profile。

完整55页/全部模态、背景透明帧及500ms、终端与冲突整页配对、完整系统阴影仍未完成。Windows10按用户要求排除，跨屏DPI保留未验证。
