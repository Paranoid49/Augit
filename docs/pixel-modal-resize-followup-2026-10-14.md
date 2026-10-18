# 2026-10-14 独立模态尺寸变化接续

本批次修复已打开Settings调整尺寸时丢失输入焦点的问题，未完成55页双端像素验收。

## 基线与整改

分支`dsh`，HEAD `3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6`。已有144个已跟踪文件的混合修改及未跟踪文件保持原状，没有回滚、暂存或提交。开始时空白检查与9项共享资产检查通过。

规格要求局部更新保留焦点。权威`SettingsEditor.java:379–390`将固定抬头与正文分离；Augit宿主`ShellModalHost.PresentCoreAsync`对同一key只调整已有窗口，不创建快照。但网页此前每次矩形变化均隐藏表单，导致输入失焦。

真实外壳新增回归先在旧实现失败：Settings物理宽度1820→1750，隐藏状态变化2次、搜索焦点丢失，HWND不变。修后网页单独保存已呈现表单身份，同一key更新尺寸或DPI时不隐藏、不等待截图帧；新表单仍保留捕获路径，关闭清空身份。背景内容变化时的捕获仍可能产生透明帧，本批次没有解决该项。

修改文件为`web/src/live-data.js`、`tools/audit/run-pixel-remediation-shell.cjs`及接续文档；未修改布局或宿主协议。

## 验证

- Windows11 x64、DPR1.75真实外壳浅深×UI12/14/40六组通过，约35秒。每组实际缩小40逻辑像素，检查尺寸生效、原窗口不变、零隐藏状态变化及搜索焦点保留；同时执行已有背景刷新、嵌套返回和关闭释放断言。[几何记录](../artifacts/pixel-followup-2026-10-14/resize-matrix/geometry.json)，同目录`resize-*.json`保留专项数据。
- `live-shell --settings-state-only`12项通过，约5秒；`native-modal-state.test.cjs`2项通过；两个修改JS语法和`git diff --check`通过。
- Debug构建0警告、0错误，2.81秒；9项共享资产通过。未改C#逻辑，不重复Shell全量测试。
- PyCharm隔离参考再次核对2026.2.1／262.9437.214，实际Settings显示Islands Dark、Microsoft YaHei UI12、Zoom100%。本次无防火墙遮挡，但工具原始输出为JPEG，只记可见状态，不计无损像素通过。[来源与SHA256](../artifacts/pixel-followup-2026-10-14/reference-source.json)。工具截图宽高不当作物理客户区尺寸。

| 文件 | 修改前MD5 | 修改后MD5 |
| --- | --- | --- |
| mockup.js | `3EEACB9FE4AA6B29CBE0F3495E426CC5` | 不变 |
| mockup.css | `5C1D95FC4479C3DC8BB22D5869324890` | 不变 |
| live-data.js | `F9F6922B29C334F35C9884A536BA3A35` | `404AF1E77653319120C6CA1654569AB6` |
| bridge.js | `F297BF8769E249CA0D804F4F4EEEF5A9` | 不变 |

六组直接读取当前web源码，构建随后同步Debug镜像。不宣称Release性能、Windows10或真实跨屏通过。

## 资源与复验

实例PID21168（修前失败）、13164（修后单组）、12220／14828／16388／5064／17584／10504（六组）均由finally退出并删除独立profile。六组端口57718／63577／57952／59624／51404／50422随实例释放。PyCharm参考PID18912及子树已停止。正式截图/JSON保留，无新增依赖或常驻服务；前轮审批拒绝的旧临时文件未重试删除。

```powershell
# 启动一次性真实外壳回归；finally自动停止实例并删除profile。
node tools/audit/run-pixel-remediation-shell.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe artifacts/pixel-followup-2026-10-14/resize-matrix --reference-fixture
# 异常中断时，只停止驱动打印的本次实例及子树。
taskkill /PID <AUDIT_PID> /T /F
```

剩余：55页同数据/视口/焦点无损配对、全部模态状态、终端/冲突逐行配对、背景刷新延迟与透明帧、系统阴影定量、跨显示器DPI及Windows10实机。六组通过不代表以上结案。
