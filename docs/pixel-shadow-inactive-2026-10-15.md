# 非活动窗口阴影取证（2026-10-15）

## 结论与边界

整体整改未完成。本批仅扩展审计工具并补原始屏幕证据，没有修改产品阴影或主题。Windows 10 已按用户要求排除本次验证，跨屏 DPI 保留未验证。当前机器为 Windows 11、175% DPI。

深色非活动 Settings 与 PyCharm 2026.2.1 参考的右下角，64物理像素范围的三个外侧象限共12288像素，背景完全相同，阴影暗化量最大通道差1级RGB，低于3级容差。左上、右上、左下分别有5746、5312、1139个背景不同像素，不计整角通过。所有同背景子集最大差均1，但不得筛除不同背景后宣称完整验收。

128物理像素范围每角49152像素；右下仍有8794个背景不同像素，四角均不计通过。圆角内侧、完整同背景四角、浅色参考非活动状态仍未完成。参考原图有鼠标光晕，位于取消按钮及下方；不属于通过的右下64像素外侧角区，不能把整条底边最大变化当作纯阴影。

四边中央半段的统计进一步确认活动与非活动必须分开比较。下表顺序均为左／右／上／下；暗化量是距边缘1物理像素处RGB通道平均暗化量的中位数，范围是中位数大于2级RGB的最远物理距离，不是高斯半径。所有128像素采样尾部均收敛。

| 主题、应用、状态 | 近边暗化量 | 范围（物理px） |
| --- | --- | --- |
| 深色 Augit／PyCharm，活动 | 4.333／4.333／3／7.667 | 18／18／5／68 |
| 深色 Augit／PyCharm，非活动 | 3／3／2／5.667 | 6／6／0／52 |
| 浅色 Augit，活动 | 42／42／27／76 | 55／55／37／119 |
| 浅色 Augit，非活动 | 30／30／16／55 | 46／45／17／113 |

这两项统计在深色双端相同，只说明所述统计一致，不等于四边每个像素一致。

## 工具、证据与失败记录

- `capture-screen-region.ps1` 默认仍强制活动窗口；非活动采集必须显式指定已观察的前景窗口句柄和进程，并拒绝前景矩形覆盖采样区。采样前后检查目标实例、DPI、边界和前景身份稳定；输出活动状态，仍保留 `unoccluded:null` 供人工复核。
- `compare-shadow-corners.py` 拒绝窗口／背景或双端活动状态混用；旧元数据仅在明确记录前景稳定时按活动状态读取。
- `capture-shadow-shell.cjs --inactive` 使用本批独立空白浏览器转移焦点，模态关闭后重新取得焦点窗口身份，重复采背景核对哈希；finally关闭浏览器和测试实例并删除隔离profile。
- `artifacts/pixel-followup-2026-10-15/shadow-inactive-{light,dark}-paired/` 保存Augit活动／非活动原图、稳定背景及 `profile-{active,inactive}.json`。
- 同目录 `shadow-inactive-reference-dark/` 保存参考原图。非活动Settings SHA256为 `306ab893410b4476aac5019e50a3686ac74df3ab24696f6ba2d4eafa9589b290`；重复非活动背景均为 `f202ae91843a18ef76f12c050d6c60eb73fbb587772cfbff7895d7d20dc98326`。深色双端原图已目视，右下通过角区无遮挡。
- 同目录 `shadow-inactive-dark-{64,128}.json` 是量化结果；两组完整四角结论均为false，不能据局部通过改写整体结论。
- `shadow-activation-guards/` 保存真实采集器5项测试结果。`shadow-inactive-light/` 因焦点窗超出桌面失败；`shadow-inactive-light-position/` 因关闭模态后前景身份变化失败，均不计配对通过。随后调整位置并重新启动焦点窗口取得完整浅色证据。
- 参考背景初次焦点浏览器PID17400未取得前景，被采集器拒绝；重试PID8844后采集成功。Computer Use对该Chrome窗口的激活返回归属错误，未绕过；随后只读采集确认其实际已为前景。一次Python管道换行转义、一次向原始PNG工具传转换后图像失败，修正输入后生成结果，失败不计测试通过。

## 验证与资源清理

真实采集器5项通过（7.518秒），角部定向6项通过；真实双主题背景重复哈希一致，模态关闭释放检查通过。本批不触发产品全量测试或构建。产品四运行时MD5前后不变：mockup.js `DB12568D024253582D1C09B5DA1CD5AE`，mockup.css `439C96E4A5696967DC702CAC9B9A0850`，live-data.js `DB52470B2BAB434DE4E463086A39FA6D`，bridge.js `F297BF8769E249CA0D804F4F4EEEF5A9`。混合修改未回滚、暂存或提交。

Augit PID12800／11876／1136／10996／8916、焦点浏览器PID19640／3432／11016／10336／9988／13060／2744／17400／8844及参考PID9060均已退出；参考按父进程子树停止，路径复查无残留。端口64291／55408／55324／57680／65110无监听。隔离目录Augit-shadow-pair-UxGxti／HdYtAT／4xU0pL／lGvHid／GN6eJA已不存在。未重试历史被拒绝的清理，正式证据保留。

```powershell
$shadowPlaywright='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
# 使用新的输出目录；驱动finally停止本次实例及焦点浏览器。
node tools/audit/capture-shadow-shell.cjs $shadowPlaywright artifacts/shadow-inactive-new Dark --inactive
python -B tools/audit/test_shadow_corners.py
node --check tools/audit/capture-shadow-shell.cjs
# 参考启动沿整改接续记录§7的隔离PYCHARM_PROPERTIES和Start-Process命令。
# 异常停止只填写本次启动返回的PID，不复用历史PID。
taskkill /PID <本次PID> /T /F
```

下一步需取得相同背景、相同状态的剩余角区及浅色参考。55页／全部模态同态配对、终端及冲突整窗配对、刷新完整逐帧与延迟仍是总体缺口；终端行高规范裁决仍待用户回复。
