# 系统阴影完整外沿量化（2026-10-15）

## 本批结论

上一轮为有效进展：产品补齐冲突当前行号并完成定向与真实保存回归。本轮推进系统阴影，新增同尺寸完整外沿证据，不修改产品阴影参数。整体目标仍未完成。

现有四边中央中位数和四角局部比较不覆盖所有外沿像素。本批将实际Settings取证尺寸设为参考DWM外框1555×1215物理像素、DPI168，使用真实空白正文背景；原始系统PNG没有缩放、平移或替换像素。比较时仅按两端DWM左上角对应整数偏移。

128物理像素外沿分成四条完整边带及四个角部象限，互不重叠，共774656像素。浅色活动态下，左下和右下各16384像素背景完全一致，暗化量通道差均0。其余六区仍有背景不一致，不计完整外沿通过；圆角落在DWM矩形内侧的部分未被该工具覆盖。不能把本批两个外侧角象限与此前每角三个象限直接当成相同采样面积。

| 区域 | 背景不同像素 | 暗化量最大通道差 | 超3级RGB像素 |
| --- | ---: | ---: | ---: |
| 左上 | 3456 | 0 | 0 |
| 顶部 | 51959 | 5 | 311 |
| 右上 | 3456 | 0 | 0 |
| 左侧 | 256 | 2 | 0 |
| 右侧 | 256 | 2 | 0 |
| 左下 | 0 | 0 | 0 |
| 底部 | 256 | 6 | 70 |
| 右下 | 0 | 0 | 0 |

全部同背景像素均未超过容差，但没有筛除背景不同像素后宣称整组通过。原始背景目视确认：实际上方有横向分隔线，参考有纵向硬换行辅助线；旧参考背景还有鼠标光晕，必须继续按完整原图核对。该证据不支持凭当前数值继续调整DWM阴影参数。下一步为相同真实背景复采、浅深活动/非活动组合及圆角内侧证据。

## 工具与验证

- capture-shadow-shell.cjs新增--reference-frame，读取原始系统截图元数据控制取证尺寸，并在采集后严格断言系统外框宽高；四周统一保留128物理像素。
- compare-shadow-ring.py要求同尺寸、同DPI、同活动状态，复用原始PNG/哈希/实例稳定性门禁；不允许沿边归一化。输出八区全部差异、背景不一致及有限定位样例，拒绝覆盖证据，始终不以外沿单组冒充全阴影。
- test_shadow_ring.py五项通过：面积无重叠、八区远端单像素缺陷、同暗化量不同背景拒绝、整数位置差可比较但尺寸/状态差拒绝、容差与内部排除。
- 本次浅色真实窗口、关闭后两份背景哈希一致，模态释放与无前端异常通过；JS语法及git diff --check通过。未运行产品全量套件或构建。

证据位于artifacts/pixel-followup-2026-10-15/shadow-full-frame-light/，参考沿用shadow-uniform-reference-light/。ring.json为初次结果，ring-provenance.json补充源路径及哈希，不覆盖原结果。完整比较返回失败是已知证据缺口，不写为命令通过。

本批启动隔离参考PID21764，尝试在Editor/General/Appearance定位Show hard wrap and visual guides。工具观察中焦点不稳定，开关未确认改变，未按此操作新增参考原始证据、未修改系统安全设置。退出前未按Apply或OK；参考仍需重新观察后采集，不能由点击意图认定背景已调整。

## 哈希与清理

产品文件未改，MD5仍为mockup.js BE21F21B0D6F6036844ADF51BD24DB46、mockup.css 0A62EE8E629016699206B3B66D7D38B1、live-data.js 00E7ECB134CBD0939313286BE7C92E38、bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。混合工作区未回滚、暂存或提交。

Augit PID19748、端口50471已退出/无监听，Augit-shadow-pair-UjLpJw目录删除并核验。参考PID21764及其子树19504/5264/4040/21316/23324/3016/21208已停止，参考路径无残留。未重试历史被拒绝的清理。Python使用-B，测试图片仅内存，正式截图和JSON保留。

Windows 10已排除，跨屏DPI未验证。55页和全部模态、背景刷新透明帧与延迟、终端/冲突逐行及完整系统阴影的原目标不变。

启动与停止命令：

    $pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
    node tools/audit/capture-shadow-shell.cjs $pw artifacts/shadow-ring-manual-new Light --uniform-background --reference-frame artifacts/pixel-followup-2026-10-15/shadow-uniform-reference-light/settings.png.json
    python -B tools/audit/test_shadow_ring.py
    python -B tools/audit/compare-shadow-ring.py <实际窗口PNG> <实际背景PNG> <参考窗口PNG> <参考背景PNG> --output <新的结果JSON>
    # 驱动正常由finally停止，异常只停止本次返回的PID。
    taskkill /PID <本次Augit或参考PID> /T /F

参考启动使用整改接续记录的PYCHARM_PROPERTIES隔离配置与pycharm64.exe fixture命令，立即恢复启动环境变量。

最终尺寸断言新增后的实机复跑shadow-full-frame-guard在系统截图前被前景门禁拒绝，不计尺寸断言已在该次实机执行。PID12356、端口51478和Augit-shadow-pair-JqpfV3均由finally清理并另行核验。此前有效原图的1555×1215外框已由完整比较器严格校验。ring-provenance.json返回1，表示完整外沿未通过；其来源哈希与原始PNG逐项核验。
