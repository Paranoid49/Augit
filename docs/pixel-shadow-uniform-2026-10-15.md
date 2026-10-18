# 同背景阴影接续

日期：2026-10-15。仅修改取证驱动，不修改产品阴影；整体未完成。

参考隔离进程PID2480复用前批实例，最大化主窗并把Settings下移40逻辑像素；背景为真实notes.txt空白正文。原图及两份背景位于 `artifacts/pixel-followup-2026-10-15/shadow-uniform-reference-light/`；两份背景SHA256均为670c1a18d357e152cfbfd2c324817aac1bfd84d06484d78ae6c823e51b0e6536。采集后停止PID2480整棵子树，复查无该路径残留。

实际驱动新增--uniform-background，使用真实文件查看器关闭侧栏，不注入背景，不改PNG。首组实际在shadow-uniform-actual-light，背景重复哈希一致。原始PNG已经目视。shadow-uniform-light-64.json：浅色活动窗口、175%下，下方两角各12288个外侧像素背景完全一致，最大RGB差2，低于3级容差。上方各384个背景像素不同，原图显示查看器分隔线经过采样带，不计完整四角通过。没有筛掉不同背景再宣称通过。

--position-window为最多120秒的人工窗口定位等待，完成标记只用于继续脚本，finally始终清理进程。Computer Use调整实际窗口边缘两次均被WebView2子窗口归属检查拒绝，未使用其他原生输入方式绕过；之后前景身份门禁拒绝采集。shadow-uniform-positioned-light不计成功证据。

实际PID3832/9508、端口60861/49837已退出/无监听；Augit-shadow-pair-2uOWLj/QW4Vna临时目录不存在。参考与实际进程本批均清理，未重试历史被拒绝删除的目录。

```powershell
$pw = "C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright"
node tools/audit/capture-shadow-shell.cjs $pw artifacts/shadow-uniform-manual Light --uniform-background
# 正常自动关闭；异常仅使用本次输出的PID。
taskkill /PID <本次AUDIT_PID> /T /F
```

剩余：四角全背景一致、128px远端、圆角内侧及全部活动/非活动状态。浅色下方两角通过只补局部证据，不代表系统阴影完整验收。
