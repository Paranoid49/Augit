# 终端基础行距整改（2026-10-15）

用户已明确裁决：按PyCharm实机行高整改，并同步规范。此前待确认项由此解除；不再沿用22px或字号乘1.7的要求。整体像素整改仍未完成。

## 实现与实测

`live-data.js`将xterm的lineHeight从1.7改为1.0，使用字体基础行格；字体、字号仍读取原有等宽设置。`ux-spec.md` §7.16和设计系统同步记录参考JetBrains Mono13、基础行距1.0、约18逻辑px。共享视觉稿／运行时CSS的静态终端行距改为18/13，不再继承普通正文22px。没有改正文、Diff、冲突和标题栏行高。

175% DPI、真实WebView2深浅主题均由约29.1667逻辑px降至17.1818逻辑px；屏幕总高／行数的物理行格约30.0682px，包含整体尺寸取整。字号17时为22.2500逻辑px，恢复13后回到17.1818。静态终端13px字号计算行高为18px。参考约18px尚非相同输出逐行精确测量，不能把相同配置或接近行格写成逐像素一致。

证据位于 `artifacts/pixel-followup-2026-10-15/terminal-lineheight-verified/`：深浅PNG、palette.json包含16行受控ANSI内容、颜色索引、实际行格和17→13字号恢复结果。深色PNG已目视，行内中英文未裁切；截图只显示末尾行，完整16行由buffer断言验证。受控样本直接写xterm，不发送Shell命令，不计PyCharm双端同态配对。

## 定向验证

真实专项通过：深浅ANSI16、基础行距1.0、默认行格17–19px范围、主题取消恢复、设置后同一终端节点保留、13→17→13字体重新度量、静态终端行格、关闭终端。ANSI配色单测1项、三个修改JS语法、9共享资产检查通过。旧live-shell中固定旧字体25／34px的断言同步为基础行距和随字号增长；本批未重复执行全量live-shell或构建。

首次真实采集在设置提交后的宿主重挂期间截图报节点分离；仅对该明确错误增加最多3次重新定位截图，其他错误直接失败。第二次通过，补充字号恢复及静态行格后第三次专项通过。一次批量编辑脚本解析失败，未执行写入；修正换行后重新应用。失败不记为通过。

| 运行时文件 | 修改前MD5 | 修改后MD5 |
| --- | --- | --- |
| mockup.js | DB12568D024253582D1C09B5DA1CD5AE | 不变 |
| mockup.css | 439C96E4A5696967DC702CAC9B9A0850 | E9D173258AFD662A66ABC90EDB0B9734 |
| live-data.js | DB52470B2BAB434DE4E463086A39FA6D | 50B86BC40F3F003A8FE292888DC930BC |
| bridge.js | F297BF8769E249CA0D804F4F4EEEF5A9 | 不变 |

## 启动、停止与清理

```powershell
node tools/audit/live-terminal-palette.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/terminal-lineheight-new
node --test tools/audit/terminal-palette.test.cjs
powershell -NoProfile -File tools/audit/verify-ui-assets.ps1
# 脚本finally停止本次实例和Shell子树；异常中断只填本次打印的PID。
taskkill /PID <本次PID> /T /F
```

三次Augit PID18284／21312／12528均退出，端口57194／53900／52838无监听，临时目录Augit-terminal-colors-tRG8qf／4OwOIu／AG2nAg由finally删除。浅色参考取证因焦点PID16436未成为前景而被拒绝，只有活动Settings图，不计完整配对；参考PID8744及子树已停止，参考路径复查无残留。未操作安全通知，未重试历史被拒绝的清理；未回滚、暂存或提交混合修改。

仍需相同输出的终端逐行双端对照、完整冲突配对、55页和模态状态、刷新完整逐帧与延迟、剩余阴影角区。Windows 10按用户要求排除本次验证，跨屏DPI保留未验证。
