# 无 CDP 设置阶段、正常入口刷新与阴影（2026-10-17）

## 范围与入口

本批接续已有混合修改，不提交或回滚。远端按实机调整并保留 Push URL 已获用户批准；Windows 10 排除本轮，跨屏 DPI 未验证。实机均经 `tools/start-augit.ps1` 强制非增量构建，使用真实配置副本、默认 Session 和系统缩放。下述证据位于 `artifacts/pixel-followup-2026-10-17/`，不得把诊断结果登记成整体完成。

## 无 CDP 设置

ETW 的随机名 `logman create trace … -ets` 返回权限不足；未提权、没有成功会话或遗留 ETL，后续查询为空。改用有限原生阶段记录：每窗最多 64 条阶段、Stopwatch 时间戳/频率、UTC 锚点和线程号。`AUGIT_SETTINGS_TRACE_DIRECTORY` 只接受已存在绝对目录；呈现及释放后异步 CreateNew 导出，失败不影响显示。只保留已知消息名，不记录输入内容；未引入依赖或改变窗口等待顺序。进程紧接关闭时可能来不及导出，因此缺少文件不能证明关闭通过。

- `settings-passive-stage`：PID 26116，DLL SHA256 `F6C92ACB1DDB47BE5E099E3E7C70DFB1BA13CEFE4CA6E6DD9F02CDE341203AF5`。点击 UTC 毫秒区间 1792206994673–1792206994820；原生 presented 约 4039ms，控制器可见调用 971.07→3762.08ms（2791ms）；创建约88ms、脚本注册约152ms、捕获约133ms。25秒录像及原生阶段保留，但原生阶段不等于最终桌面呈现。
- `settings-visible-guard`：仅在已可见时省略赋值的候选未命中（实际 controller-hidden）；PID 20492，DLL `4BBC7714F4381D46847F967BB9E6CC1F1A170644799603F8ED31ED395C2D0A5F`，presented 约2685ms，创建约2150ms。没有收益证据，候选已精确撤销，后续正常入口强制重建。点击在录像结束之后，该录像不能证明开窗过程。
- 桌面工具只暴露主窗，owned Settings 无可用独立输入目标，未伪造 HWND 或改用其它输入通道。无 CDP 完整关闭和实际字形仍未验证。
- modules.json 显示本实例和三个 WebView2 进程加载 `C:/ProgramData/A-Volute/A-Volute.Nahimic/Modules/Scheduled/x64/AudioDevProps2.dll`。尚未确认因果，没有停止服务或改系统设置。依产品规格§2，此环境性能结果只作诊断，不能最终验收。

## 正常入口阴影

`capture-shadow-shell.cjs --daily-launcher` 使用真实配置副本及正常启动器，拒绝软件渲染、强制背景、定位/参考框、网页覆盖等条件。旧 `--normal-entry` 仍为隔离入口，不能混称正常启动。

`shadow-daily-current`：PID12824，端口62482，DLL `64B8575C12779C84DE4F846BC0F9209EB3B89B8CB345382CD629C54024851CD9`。Light、UI12、DPR1.75；搜索文字实际 Microsoft YaHei UI，15 glyph。截图2076×1586，窗口1820×1330，图内框 `[128,128,1948,1458]`。关闭背景两份哈希相同，owner恢复，snapshot和cover归零。

右近边最大中位差0.5、底边3 RGB，仅局部容差3通过。四角各49152像素，与10/15浅色参考的背景差像素分别3903/3840/4235/7443；同背景子集最大差2，完整四角因背景不同仍不可比较。没有缩放、剔除差异背景后宣称完整阴影通过。

## 真实文件触发刷新

`refresh-daily-file.cjs` 创建本批40行临时文件，正常取消 Git 初始化提示后打开正文及跳转行，写入四次 generation 触发实际监听，不注入 DOM 标记。首批 `refresh-daily-file` 因未处理初始化提示失败，已保存 failure 并清理；修正仅在审计流程。

成功批 `refresh-daily-file-cycle`：PID28452、端口62079，DLL同上述64B857…；四次完整刷新观察上界614/427/431/416ms，首次仍超500ms目标。首次cover capture211ms、controller87ms、navigation124ms、ready94ms；阶段存在包含关系，不能直接相加。

完整585×252矩形283帧（最大间隔32ms）逐帧以RGB容差2比较：156帧与before.png相同，127帧仅78像素变化，范围 `[218,107,221,133]`；worst-frame.png目视为输入光标。未裁去边框，该样本没有检出透明表单；`fullPresentationVerified=false`，不能外推所有模态、主题或采样间隙。录像SHA256 `C21F307B30A8B46F772D9E0B22189E0952E647868A6D984F65138967ECCD802C`。关闭后snapshotCount=0、captureCoverWindow=0、ownerEnabled=true。

## 验证与资源

此前20项Shell定向、17项相关JS、3项完整帧Python、C#定向格式、Debug25资源镜像、9项共享资产及diff检查通过；没有全量测试。最后消息白名单修订后的测试和最终DLL另补登记，不能由旧产物推断。四网页MD5未变：mockup.js=1186D90A02D8BC87F0F2764C13D0440F；mockup.css=6E79CBFE5FA5629AC900EAF05D109A43；live-data.js=D64AB5C4E97AA7D3ED60F456555A34AE；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

`passive-shadow-refresh-cleanup.json` 独立检查5实例、3端口、5临时目录均零残留；记录器11000/28528/23580/22820/9124均已退出。未触碰历史拒绝清理目录。

启动：`powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1`。优先正常关闭；异常时仅使用核实属于本批的 `taskkill /PID <本次PID> /T /F`。

## 未完成边界

以下保留本阶段失败与工具限制。后续冲突参考三状态已补齐、PID23392已正常退出，临时目录仍保留；正常入口边界留白小修及完整编辑区结果见[pixel-conflict-daily-inset-2026-10-17.md](pixel-conflict-daily-inset-2026-10-17.md)。不改变设置、刷新、阴影及整体未完成结论。

## 本轮续接收尾与工具阻塞

消息名白名单补充14例回归，连同阶段与独立设置共34项通过（178ms）；18项相关JS（249.8282ms）、3项Python（0.006s）、定向C#格式、文档声明、9项共享资产、Debug25镜像及diff检查通过。历史全量快照过期，未重跑全量。正常启动器随后实际强制构建6.11秒，最终Debug DLL SHA256为8DC72AE58C3CF37D12B6B6C598A8EE26FB822BC2BBEBBE2CF9B90D7F766EC667；四网页MD5不变，Release未更新。本段产物不同于前述录像产物，不能倒推旧证据。

新增 conflict-daily-options/capture.cjs，将旧冲突取证器的 --daily-launcher 路径切到正常启动器、真实配置副本及默认profile，拒绝尺寸/主题/字体/诊断覆盖；不运行旧DOM探针。conflict-daily-current 取得三状态原始系统图和全行数据，PID10332、端口63196、临时目录Augit-merge-pair-eIWjrE已由finally清理并独立确认不存在/无监听。DPR1.75，Light/UI12/正文13，实际字符JetBrains Mono；实际窗口1890×1215，旧参考1730×1215，不能直接比较。初始图已目视；其他两图本轮尚未目视，不宣称完整配对。

参考补采使用既有PyCharm2026.2.1（262.9437.214）及新四文件副本。PID19108隐藏启动无可用窗口，已停止；PID25888正常启动后检查尺寸菜单并停止。随后PID23392使用专用参考配置的临时副本，路径C:/Users/wl024/AppData/Local/Temp/Augit-reference-size-2080ba76b5494ce5a6ec159cc9974a75；没有改用户PyCharm配置。窗口尺寸从1891×1215经原生左边框内收1物理px成为1890×1215，三个size-probe原图保留，但含指针光晕，不能充作合格参考。

统一首行焦点后，在选择窗外指针停放区域时Computer Use无法可靠识别浏览器URL，要求停止本轮。没有继续UI输入。之后精确核对本批PID及临时目录的清理命令被自动审批策略拒绝（blocked by policy），未绕过或重试其它删除通道。因此PID23392及上述临时目录可能仍在，不能登记零残留。需要下一轮恢复工具能力后先核查并清理；本批截图与JSON为证据须保留。整体goal保持active，未完成。

手动停止本批参考：taskkill /PID 23392 /T /F。确认该PID仍属于本批PyCharm后执行；临时目录仅在进程退出并核对绝对路径后使用PowerShell Remove-Item -LiteralPath删除。未暂存、提交或回滚。

慢开仍存在，无 CDP 完整关闭受输入目标限制；未获干净环境因果证据。首次刷新仍614ms，其他模态/主题/嵌套待验。55页及全部状态、终端/冲突完整逐行、系统阴影完整同背景外沿仍未完成，goal保持active。
