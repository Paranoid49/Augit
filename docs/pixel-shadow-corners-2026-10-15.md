# 系统阴影四角取证（2026-10-15）

## 结果与范围

整体整改未完成。本批只补窗口矩形外侧四角，不改产品代码。活动状态、175% DPI、深色右下角64物理像素范围的三个外侧象限，共12288个像素背景完全相同，阴影暗化量最大通道差1级RGB，低于3级容差。该局部通过不代表圆角内侧、全部四角、远端或非活动窗口通过。

扩大至128物理像素后，每角49152个像素；深色右下有8794个背景不一致像素，因此扩大范围后也不计整角通过。深色四角同色背景子集最大差均1，浅色均2，但不筛掉背景不同的部分后宣称验收。背景、主窗布局与模态尺寸尚未同态，仍需同背景采集。

旧浅色修后图（快照物理尺寸修复之前）的左下64像素范围，同色背景最大差240，513像素超容差；当前复采最大差2、超容差0。这支持此前快照尺寸修复消除了该位置的栅格干扰，不把它误判为需要再次修改系统阴影。

## 工具与证据

新增 `tools/audit/compare-shadow-corners.py`：逐角比较窗口矩形外侧三个象限，显式排除内侧内容；同时输出全部像素差、同色背景子集差、背景不一致计数。整角必须全部背景一致且差值在容差内才通过。工具核验原始屏幕PNG来源、SHA256、同实例区域和DPI、采样前后稳定性；不接收旧记录缺少稳定性字段的深色证据。未降低门槛，而是重新启动参考并采集。

所有路径位于 `artifacts/pixel-followup-2026-10-14/`；该目录沿用批次名称，实际时间以JSON的UTC时间为准，本文按本地2026-10-15登记。

- 当前Augit原图及两份稳定背景：`shadow-corners-current-light/`、`shadow-corners-current-dark/`。深浅原图已目视，无遮挡阴影区域；两份背景哈希各自一致。
- 当前深色参考：`shadow-corners-reference-dark/`。设置图SHA256为 `061e22920466aafc2a5c9563fd1ad30c9ac5e7056eb6552831c9904fa7a078ed`；两份背景均为 `f33f9d5b507dc7aff7d38ba063f6362ab7ef1b3fc48873833555b11057e3a0a6`。关闭动画首帧排除。未操作启动时的安全提示。
- 浅色参考沿用来源完整的 `shadow-reference-light/`。
- 64像素结果：`shadow-corners-light-before.json`、`shadow-corners-light-current.json`、`shadow-corners-dark-current.json`。
- 128像素结果：`shadow-corners-light-128.json`、`shadow-corners-dark-128.json`。所有整组返回未通过；这是明确的覆盖缺口，不是执行错误。

## 验证及清理

5项定向测试通过（0.166秒）：同图局部通过但全阴影保持false、四角缺陷与内侧排除、背景不同不得通过、容差及输入边界、哈希/稳定性/实例/来源拒绝。测试临时目录由TemporaryDirectory退出清理，Python使用-B。深浅真实采集的稳定背景、模态关闭释放、无未处理错误检查通过。git diff --check通过，未跑全量或构建。

产品四个运行时MD5未改：mockup.js `DB12568D024253582D1C09B5DA1CD5AE`，mockup.css `439C96E4A5696967DC702CAC9B9A0850`，live-data.js `DB52470B2BAB434DE4E463086A39FA6D`，bridge.js `F297BF8769E249CA0D804F4F4EEEF5A9`。保留混合修改，未暂存提交。

Augit PID3096/12116、端口56473/54082已退出且无监听，临时目录Augit-shadow-pair-CpEBDO/3d42a7已删除。参考PID7816及子树已停止，路径复查无残留。未重试此前被拒绝的目录清理。

```powershell
$shadowPlaywright='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
# 使用新的输出目录，采集脚本finally停止本次实例并删除隔离profile。
node tools/audit/capture-shadow-shell.cjs $shadowPlaywright artifacts/shadow-corners-new Light
python -B tools/audit/test_shadow_corners.py
python -B tools/audit/compare-shadow-corners.py <Augit窗口PNG> <Augit背景PNG> <参考窗口PNG> <参考背景PNG> --radius 128 --output <新结果JSON>
# 参考启动沿整改接续记录§7隔离PYCHARM_PROPERTIES及Start-Process命令。
# 异常停止只填写本次启动返回的PID，禁止复用历史PID。
taskkill /PID <本次PID> /T /F
```

下一步仍是同背景全角、圆角内侧和非活动状态。当前采集器强制前景，因此不能拿活动窗口截图冒充非活动证据。终端行高规范裁决仍待回复，与本批阴影结论独立。

2026-10-15接续：采集器已支持显式非活动模式，原文末段的“只支持前景”仅描述本批初始状态。当前非活动证据、扩展6项测试与剩余边界见[非活动阴影记录](pixel-shadow-inactive-2026-10-15.md)。
