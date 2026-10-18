# 冲突来源标题与正文起点整改

## 最新接续：编辑光标合成偏移已修复

2026-10-15：本批排除了上轮背景遮盖假设。conflict-title-refresh-trace记录接受左侧及全部处理均无刷新调用，snapshotRevision为0、captureCoverBytes为0。conflict-title-focus-probe记录聚焦混色、失焦恢复、再聚焦混色；CDP LayerTree证明Caret触发Overlap合成层，纵向变换为95.5物理px，独立横线层仍带0.5px偏移。

缩短SVG、裁剪和独立分层无效；按居中变换补偿在175%有效，但125%栅格失败，试验产品改动均已撤销。最终仅将冲突窗口CSS改为inset:0、margin:auto、transform:none，以布局居中消除合成重采样，保留自适应宽高和编辑焦点。没有增加猜测偏移或修改背景刷新协议。

最终证据artifacts/pixel-followup-2026-10-15/conflict-title-layout-final：三个1730×1215系统原图中，区域[2,194,1728,196]均3452像素全部为#D4D4D4，初始与参考完全一致。接受后参考鼠标光晕及完成提示仍计入差异；整窗差异依次为95542、139359、201428像素，不代表整窗通过。已目视接受左侧图。

60组栅格（五DPR×双主题×UI12/40×初始/聚焦/失焦）、72组布局和2项刷新追踪测试通过。深色真实多块滚动、视口外焦点及精确保存通过，见conflict-title-layout-save/multi.json的exactSaved=true。三状态比较新增--require-stable-separator，严格检查实际三状态全部横线；参考提示造成的完整差异仍保留。9项共享资产、JS语法、文档声明及git diff --check通过；旧全量快照过期，未用于本批结论。未重复全产品、51项模块全套或C#构建。

CSS MD5：7C2796F80F65EAC3F472A6EEC3D7A2AB → 56BC1EB2D83B203A1890B50B0C37250D。mockup.js保持1E09AB1418F80AD910ABFE4BBF524838，live-data.js保持00E7ECB134CBD0939313286BE7C92E38，bridge.js保持F297BF8769E249CA0D804F4F4EEEF5A9。

本批PID/端口/隔离目录尾缀：8004/56436/Yw56ei、2160/51305/WXUBl4、24264/54935/uUSMda、21636/53850/io5Ywl、5900/56591/QZ1P57、17180/57644/Vl8DoO、2068/51251/869Rpw、4776/63867/5vA57i。驱动finally全部回收；末次检查无这些进程、监听端口或隔离目录。浏览器专项也已关闭自身浏览器，未启动额外服务器；保留PNG/JSON作为成果。未重试历史被拒绝的清理，未暂存、提交或覆盖既有改动。实机三状态约10秒、保存约8秒，栅格和布局约一分钟，均为本机粗略观测。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$chrome='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/audit/verify-conflict-title-separator.cjs $pw $chrome
node tools/verify-ux-conflict.cjs $pw $chrome artifacts/conflict-layout-manual
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-title-manual-new $pw --light --screen-pair --reference-frame
python -B tools/audit/compare-conflict-three-states.py artifacts/conflict-title-manual-new --require-stable-separator
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-save-manual-new $pw --multi --reference-frame
# 正常自动清理；异常仅停止本次打印PID及后代。
taskkill /PID <本次PID> /T /F
```

55页及全部模态、终端完整逐行、背景刷新整表面/延迟和完整阴影仍未完成。Windows 10排除本次验证，跨屏DPI未验证。下方“接受后混色”是修复前历史记录。

日期：2026-10-15。分支dsh，HEAD为3e084a2d7ead68ba8d6d93b4d31b4d21728d16d6。承接用户已授权的PyCharm实机布局整改；未回滚、暂存或提交混合工作区。Windows 10不纳入本次验证，跨屏DPI未验证。整体目标仍未完成。

## 依据与改动

本地intellij-community提交576e32820af82f97529d70f9269726803a27c016：DiffContentPanel给标题增加customLineBottom；DiffContentLayoutPanel将含边框的首选高度纳入正文起点；DiffSplitter.paintTitleSeparator在连接带继续绘制横线。DiffUtil.getContentTitleSeparatorColor读取TEARLINE_COLOR，Light/expUI_lightScheme为D4D4D4，Darcula及Islands Dark继承值为555555。

共享mockup.js将来源标题行高从max(30,多行文字高+8)改为该值加1逻辑px，不改文字基线补偿。共享CSS移除原先被标题行高吞掉的浅灰边框，保留透明边框占位；只读SVG以同一颜色绘制连续横线，厚度round(DPR)，位置按设备像素取整。布局度量后刷新绘制，SVG置于三栏之前，保持右来源:last-child语义。design-system.md与ux-spec.md同步。

## 原图结论与失败迭代

参考仍为conflict-line-pair/reference-initial.png；最终实际为conflict-title-separator-verified/actual-initial.png，均位于artifacts/pixel-followup-2026-10-15，均为1730×1215、DPR1.75系统原图。compare-conflict-title-separator.py不平移或缩放图片，输出comparison.json。

左侧三行墨迹边界从[35,204,171,222]、[35,244,171,261]、[34,281,157,299]变为[35,206,171,224]、[35,246,171,263]、[34,283,157,301]，与参考一致。这只证明该三行边界，不证明抗锯齿和所有行完全相同。

初版CSS横线虽连续但原生193/195行混色；SVG消除混色，却仍位于193/194，而参考在194/195。尝试容器本地坐标和补充viewBox未消除此差，且本地坐标在DPR1.25专项失败，最终恢复绝对设备坐标。未加入猜测性的1px偏移。最终横线颜色、连续性、厚度改善，纵坐标仍高1物理px，不能称分隔线像素通过。

SVG最初append导致右来源:last-child失效，原生驱动及时报错；改为prepend并加入结构断言。首次大字号仍按旧标题高度绘制，已在measureConflictResolver末尾重新同步。这些失败均不算通过证据。

## 分批验证

- 最终20组专项：浅深主题、DPR1/1.25/1.5/1.75/2、界面字号12/40；验证含边框标题高、整行PNG颜色、绘制层不接收指针且不改变右来源结构。通过。
- 72组冲突布局通过；16组行填充专项通过。后续仅调整只读SVG坐标/视口，重跑20组针对性测试及原生三状态，不重复完整布局。
- 原生多块：公共正文对齐、接受/撤销、双向滚动、视口外动作聚焦和精确保存通过，证据conflict-title-save/multi.json。
- 最终原生初始/接受左侧/全部处理三状态完成身份、DPI、尺寸检查；静态浏览器通过不替代原生一像素差异。
- 未运行全产品测试、Shell测试或构建：本批没有C#、协议或外壳生命周期改动。

mockup.js MD5：69203C667E940D79782FA85B600D49B4 → 481C7E8AB6935BFC6AA4FB66C2C0E305。mockup.css：91CB995954680CA777EF336D27580103 → ED3523156692654BA60709536C3B4969。live-data.js保持00E7ECB134CBD0939313286BE7C92E38，bridge.js保持F297BF8769E249CA0D804F4F4EEEF5A9。

## 启停与资源

本批原生PID：11140、10756（失败）、13284、23848、24496、12496、11704、20440；端口58631、57421、64802、63646、54884、53205、55686、59846。对应隔离目录尾缀cilWIf、K4mRiA、aEsBuO、qiyNyL、cFGtAj、tNMfSh、IQN4wr、k7sHjK。驱动均在finally停止自身进程树并删除自己的隔离目录；浏览器专项同样finally关闭。未重试历史被拒绝的清理。截图、JSON和审计脚本为保留的验证成果。

最终9项共享资产、JS语法、17项PowerShell脚本编码、文档声明检查及git diff --check通过。文档检查同时提醒旧全量快照过期，未将其作为本批通过证据。附加conflict-title-screen-geometry/screen-geometry.json记录截图前稳定DOM：横线相对窗口y194.4844、高1.9844，正文y196.25；屏幕横线仍在193/194，后续应核对原生客户区映射，不能只改CSS添加1px。该附加运行约10秒，多块真实验证约10秒，最终20组专项约11秒；只记录工具观测的近似耗时。

```powershell
$pw='C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
$chrome='C:/Users/wl024/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe'
node tools/audit/verify-conflict-title-separator.cjs $pw $chrome
node tools/audit/capture-conflict-reference-shell.cjs artifacts/conflict-title-manual $pw --light --reference-frame
# 正常自动清理；异常仅停止本次驱动打印的PID及其后代。
taskkill /PID <本次PID> /T /F
```

仍缺55页及全部模态同态配对、终端完整逐行配对、背景刷新严格整表面及延迟验收、完整系统阴影。此批修复不能代替这些验收。

## 接续：绘制视口与状态分离

上一节记录的1px差异在初始态已消除。当前SVG使用完整浏览器视口坐标和整数设备路径，宽高向上取整到设备像素，采用crispEdges；节点仍置于conflict-columns内部并由窗口销毁，不建立全局浮层。z-index为1，避免三栏正文背景覆盖横线末排。标题和正文几何没有再次改变。

当前最终证据conflict-title-viewport-crisp/actual-initial.png与参考原图同尺寸、同坐标。区域[2,194,1728,196]共3452像素全部一致，前后三行无多余横线；三行墨迹边界继续一致。该图已目视确认。compare-conflict-three-states.py进一步保留三状态完整差异：初始104920像素、接受左侧151811像素、全部处理213397像素不同，绝不将局部一致当作整窗完成。接受后参考存在鼠标蓝色光晕，全部处理中央存在完成提示；没有删除这些差异。

接受左侧和全部处理后，实际横线重新出现混色：194行全为#E9E9E9、195行为#D4D4D4、196行大部分#E9E9E9。参考194/195除鼠标光晕外仍为#D4D4D4。conflict-title-state-matrix/pair-states.json证实两个状态的SVG路径、viewBox、屏幕矩阵及正文位置不变，不能再用猜测性的CSS位移修复；需继续核对背景遮盖恢复后的合成。初始态通过不代表状态切换通过。

试验记录：完整标题视口单独使用仍高1px；变换补偿使初始纵坐标正确但下排被三栏覆盖；增加绘制层顺序后初始通过、125%出现混色。仅修改路径矩阵、取消祖先变换、改变SVG局部原点、背景边框、独立合成层均未同时通过各缩放。完整视口方案最终通过20组全厚度PNG专项；初版专项只采一排，现已覆盖round(DPR)全部排，并以四个整数传递采样范围，解决Windows参数过长。临时页面探针已删除，不保留无效产品分支。

验证：20组最终专项通过；72组布局及51项冲突模块回归通过，仅在模块合并后执行一次。10,485,078字节混合换行样本尾部编辑216ms、编辑及保存648ms，仅是本次观测。最终深色原生多块滚动、视口外焦点、接受/撤销和精确保存通过（conflict-title-final-save/multi.json）。单独三状态比较只断言初始分隔线，其他状态全部报告，不能将退出0解释为三状态全部通过。未跑全产品测试或C#构建。

本次mockup.js MD5：481C7E8AB6935BFC6AA4FB66C2C0E305 → 1E09AB1418F80AD910ABFE4BBF524838；mockup.css：ED3523156692654BA60709536C3B4969 → 7C2796F80F65EAC3F472A6EEC3D7A2AB。live-data.js、bridge.js保持上一节哈希。

本次原生PID/端口/隔离目录尾缀：24244/53897/iZqBZX、2920/63583/dDYFjc、13972/49167/2guCSR、7924/51558/AAQZ9s、20084/59844/7OxZtj、17352/52339/ZcLUum、10788/52728/4T1P4K、2060/59340/S0ERLe、24384/60283/oETXNj、23212/54501/oP7SM0。驱动finally全部完成清理。截图与JSON保留为验证成果；浏览器及模块测试关闭自身浏览器/服务器。启动停止命令沿用上一节，新增比较命令为python -B tools/audit/compare-conflict-three-states.py。无回滚、暂存或提交。整体目标仍未完成。
