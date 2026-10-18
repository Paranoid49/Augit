# 终端自然横幅配对与句柄继承修复（2026-10-15）

整体目标未完成。本批取得PyCharm与Augit同一Windows PowerShell自然启动横幅的原始系统PNG，发现并修复审计启动方式下终端提前退出的问题。局部两行可直接配对，但整窗尺寸、工具窗位置及标题仍不同，不计55页整窗合格同态配对，也不宣称完整终端验收通过。

## 可复现功能缺陷

新驱动capture-terminal-pair.cjs不注入显示文本、不发送Shell命令，等待真实提示符。前三次捕获终端就绪但buffer为空；宿主仅收到ESC[?9001h与ESC[?1004h，随后Shell以0退出。桥接跟踪证明只有一次start和一次自然失焦序列ESC[O，没有stop或用户命令。

基础ConPTY直接启动及启动协商期失焦测试通过；同一个WinExe测试宿主经Node的ignore/pipe/inherit标准句柄启动均超时，其中inherit横幅跑到父控制台。PowerShell Start-Process直接启动通过。无条件CREATE_NO_WINDOW试验仍失败，已撤销。

保留的修复仅为STARTUPINFO显式设置STARTF_USESTDHANDLES并使用零标准句柄，让ConPTY属性提供标准输入输出，而不继承父进程的重定向句柄。原有按父控制台决定CREATE_NO_WINDOW的逻辑不变。修后Node同路径宿主通过，真实Augit自然横幅、提示符成功显示。未改变Shell选择或启动参数，没有添加NoProfile等替代行为。

## 局部逐行证据

证据根目录artifacts/pixel-followup-2026-10-15/terminal-line-pair。reference-light.png来自PyCharm2026.2.1，2430×1637；fixed/actual-light.png来自真实Augit，2450×1628。均为175%DPI浅色，原始CopyFromScreen PNG，附PID、DPI、窗口边界、前景及SHA256清单。截图已目视；参考安全通知自然消失后采集，未操作通知。Augit指针光晕位于正文中部，不遮挡终端横幅。

| 文字 | 参考墨迹宽/高 | Augit墨迹宽/高 | 宽差 |
| --- | ---: | ---: | ---: |
| Windows PowerShell | 249/18 | 243/18 | −6物理px |
| Copyright横幅 | 789/23 | 768/23 | −21物理px |

两行顶部间距参考30、Augit29物理px。字形不同，不把行顶差当作精确基线行格。采用三通道均≤80的深色墨迹门槛，未重采样；原图区域、哈希和全部边界见banner-ink.json与line-pair.json。新增浅底墨迹方向支持及参数校验，原深色模式保留，3项单测通过0.082秒。

相同文字已证实字宽仍未对齐；xterm当前行距1.0配置不能当作像素一致证明。后续应按真实参考的字符网格继续处理字宽及基线，覆盖其他字号、深色与中英文混排；不直接套用普通编辑器的CSS字距。

## 验证及清理

ConPTY相关4项通过，覆盖CMD/Windows PowerShell、启动失焦、重定向句柄及Shell/子进程回收。重定向测试进一步按CreateNoWindow=false匹配Node路径后单项通过。真实自然横幅采集通过；最终Shell构建0警告0错误2.84秒。限定C#格式、JS语法通过，未跑全量套件。一次工具封装解析失败未写入；等待提示符超时及分离进程试验失败均保留，不记通过。

参考PID2212及PowerShell18620、参考子树已停止；Augit22568/3552/15476/16976/15088/22580及测试宿主9396/14680/7368/15924/4464/3172均退出。端口51781/62245/64721/49694/64043/51460无监听，Augit-terminal-pair-t3BCaw/R9r5tc/QTgaVJ/vCuwIE/4F7ykV/W2cC1j已删除。正式PNG/JSON保留，未重试历史拒绝清理。

四网页MD5均未改变：mockup.js DB12568D024253582D1C09B5DA1CD5AE；mockup.css E9D173258AFD662A66ABC90EDB0B9734；live-data.js 50B86BC40F3F003A8FE292888DC930BC；bridge.js F297BF8769E249CA0D804F4F4EEEF5A9。分支dsh、HEAD3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6；未暂存、提交或回滚混合修改。

```powershell
dotnet build tests/Augit.Terminal.TestHost/Augit.Terminal.TestHost.csproj -c Release --no-restore
dotnet test tests/Augit.Infrastructure.Tests --no-restore --filter FullyQualifiedName~ConPtyTerminalSessionTests
node tools/audit/capture-terminal-pair.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/terminal-pair-new
# 驱动finally停止实例子树并删除profile；异常中断只填本次打印的PID。
taskkill /PID <本次Augit_PID> /T /F
# 参考启动环境和命令沿用整改接续记录§7；停止只针对本次参考PID。
taskkill /PID <本次参考PID> /T /F
```
