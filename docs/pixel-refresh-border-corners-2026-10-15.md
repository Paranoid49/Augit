# 背景刷新直边与圆角差异分离

日期2026-10-15。整体目标仍未完成，Windows 10排除本次验证，跨屏DPI保留未验证。保留混合工作区，未回滚、暂存或提交。

## 本批试验与决定

基于此前内部源图像素一致、差异集中原生边缘的结果，分别试验：

1. 仅对模态父窗WM_ERASEBKGND调用黑色库存画刷FillRect，捕获子窗不变。首次构建因未检查返回值触发CA1806，修正后构建通过。实际浅色440帧中45变化帧，最严重3773像素、最大134级RGB，首次529.43ms；右/下近边阴影相对基线0/0，但并未消除闪亮，已撤销处理分支和两项P/Invoke。
2. 保留四边1px框架扩展，仅设置DWM属性34为0xFFFFFFFE以隔离其边框着色。浅色427帧中50变化帧，最严重134像素、最大241级RGB，首次541.56ms；右/下近边阴影仍为0/0。仍未满足严格整表面条件，且改变正常边框外观，已撤销设置及P/Invoke。

两份原始FFV1录像、屏幕PNG、身份清单及分析位于artifacts/pixel-followup-2026-10-15/refresh-frame-erase-light与refresh-no-dwm-border-light。最大采样间隙均50ms，无重复时间戳。没有减小验收表面、提高2级RGB容差或删除失败帧。

## 新的定位证据

新增locate-refresh-frame-difference.py，固定首帧与报告中变化像素最多的失败帧，完整解码为原始尺寸RGB，再按四个16×16角区、剩余16px边缘及内部统计。分区总数必须与原整表面报告严格相等。提取失败原图并保存worst-frame-location.json；只定位，不改变验收范围。

- 擦除试验第50帧3773差异像素：四角132、其余边缘3641、内部0。
- 禁用DWM边框第48帧134差异像素：四角134、其余边缘0、内部0。

这支持把直边亮变和圆角合成残差分开继续调查，不能称DWM边框是唯一根因，也不能仅凭该次单主题样本保留禁用方案。原生边框及阴影的正常外观仍需参考验证。3项分区测试覆盖完整不重叠计数、排他边界和越界拒绝，通过。

## 恢复与清理

最终产品恢复本批开始状态：四边1px扩展，无新增擦除分支、无DwmSetWindowAttribute。恢复Debug构建0警告/0错误、2.55秒，git diff --check通过。未运行全产品套件。网页MD5未变：mockup.js 69203C667E940D79782FA85B600D49B4、mockup.css 91CB995954680CA777EF336D27580103、live-data.js 00E7ECB134CBD0939313286BE7C92E38、bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。

Augit PID17628/11584、录屏PID12572/23680已退出，端口51163/62598无监听；隔离目录Augit-shadow-pair-xuQ9FZ/kwuSu1已删除。解码进程均已完成，审计脚本、录像、PNG和JSON作为成果保留，没有新增临时文件；不重试历史被拒绝清理。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/audit/capture-shadow-shell.cjs $pw artifacts/refresh-border-manual Light --refresh-video
python -B tools/audit/verify-refresh-surface.py artifacts/refresh-border-manual --require-stable
python -B tools/audit/locate-refresh-frame-difference.py artifacts/refresh-border-manual
python -B tools/audit/test_refresh_frame_regions.py
# 正常由finally清理；异常停止只使用本次驱动报告的PID。
taskkill /PID <本次Augit_PID> /T /F
taskkill /PID <本次recorderPid> /T /F
```

55页/全部模态、背景刷新透明帧与延迟、终端/冲突逐行和完整阴影四项仍未全部达成，目标保持活动。
