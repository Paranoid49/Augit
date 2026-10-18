# 正常模态源图与系统边缘差异（2026-10-17）

后续订正：下文背景同坐标对比采用业务裁切原点，未转换到实际屏幕下方背景位置。新原生坐标证实两者相差12px，客户区定位已修复；顶部背景混合相关性及仍未解决闪帧见[定位整改](refresh-client-origin-2026-10-17.md)。旧原始证据保留。

本批没有修改产品渲染逻辑。新的证据表明，原先统一称为撤盖后边缘闪亮的变化，至少包含初始系统图已有的彩色和深色边缘像素消失；基线稳定不等于基线正确。尚未确定这些像素的来源，也未修复透明帧或整体无闪。

## 现有证据重新定位

对上一批正常入口 refresh-status-three-parallel-burst 的原始 before.png 与 worst-frame.png 做同坐标比较：187个RGB容差2以上的变化像素中，77个位于第二行，56个位于第二列；基线部分像素为(47,47,47)或棕蓝色，最差帧对应位置变为灰色。edge-pixel-map.png保存完整并列图和差异图，不平移、不缩放、不排除边缘。该分类不证明来源，也不能将变灰自动判为改善。

核对既有记录后没有重复WM_NCPAINT拦截、黑刷擦除、禁用DWM过渡、内缩、裁切、固定等待、位图遮盖或will-change实验。它们已有失败或阴影退化证据。

## 新正常入口证据

证据位于artifacts/pixel-followup-2026-10-17/refresh-edge-source与refresh-edge-background-source。均由tools/start-augit.ps1强制非增量Debug构建、真实配置副本、默认Session、正常DPI启动，附CDP。只读诊断在首次遮盖前保存整页业务PNG、DOM几何与宿主实际裁切，随后取得系统原始截图；第二组还读取实际已加载的主窗背景PNG，SHA256与window/modal-state的backgroundHash严格核对。没有修改DOM、字体、焦点或捕获参数去配准。

- 原始业务图为RGB、不含alpha，2881×1717。主窗背景资源亦为RGB、2881×1717；宿主业务控制器为2880×1716，保留原始取整差，不压缩图片。
- DOM表单为x=656.1428833、y=418.5714417、334×144逻辑像素，DPR1.75、背景rgb(247,248,249)、borderRadius=0、opacity=1、nativeWindow=true。
- 宿主实际源图裁切[1148,733,1733,985]，系统矩形[1136,721,1721,973]，完整585×252。使用宿主物理偏移，不把DOM半像素取整当作配准。
- 两批系统基线SHA256完全相同：58B236C4ECF487C39E30EAEA6C9CDF4803CFB72AED37D1E1D5B6F44A348E0B02。
- 两次源图到系统图均有3578个容差2以上差异像素：外围3像素带3424，内部154；其中105个为源图近中性、系统图彩色的边缘点。分区仅定位，排除像素0；系统描边本就不在源图中，3578不能全算产品错误。
- 例如客户区(34,1)和(75,1)：业务原图均为(247,248,249)，系统图均为(47,47,47)。实际背景PNG同坐标分别为白色与黑色，因此不支持所有异常点都是背景同坐标直接透出的简单解释；尚未证明底层来源或合成因果。
- 两图采样不原子同时，捕获本身可能影响时序。source-screen-pair.png与source-screen-background.png只用于原尺寸并列诊断，不作参考软件配对验收。

完整录像第一批1152帧、最大间隙38ms，第二批1354帧、最大间隙39ms，均585×252、无重采样；最差仍265个变化像素。完整分析保留所有像素，fullPresentationVerified=false。最后撤盖分别597.4/481.6/478.9/479.7ms及613.8/514.3/467.1/467.3ms，首次超时且存在后续超时，不能宣称500ms普遍达标。

runtime.json再次确认DPR1.75、界面Microsoft YaHei UI12、正文实际随包JetBrains Mono13。实际执行live-data SHA256仍31964BB95E93477B66EC159588AC14B805B3339DA3BAC69EA52B8B2EFC8A1BFC；实际映射Shell仍4CAC07952E541B48BCF069F0F7DDA4B03783A8BB4F12BF86329129D9148C9F75，Infrastructure仍A08EB839CC37A2E71BFB5CF8C03C2BCD4FA74BE596CCECDC9D2222E12C27964B。Nahimic仍映射，无注入正式性能验收仍缺失。Release未更新。

四网页MD5未变：mockup.js=0EEBB52037A8EEABDB81DA777E8C1FFE；mockup.css=00FA3357EC646E91030A9DFDD925DAA9；live-data.js=D50C01AF5311343AC7F8E03FA2E2F0FC；bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

## 工具、测试与清理

新增capture-modal-edge-source.cjs及定向测试；refresh-daily-file.cjs仅增加显式--trace-edge-source入口，默认不捕获额外源图。新增compare-modal-edge-source.py及测试：核验PNG哈希、源/系统方法、进程与窗口身份、DPI、完整矩形和整数裁切，拒绝越界/伪造来源，结果拒绝覆盖。3项采集工具、7项既有刷新驱动、3项Python用例覆盖通过；其中最后新增资源哈希/会话释放用例单独跑3项采集工具测试通过174ms。未跑产品全量测试。

首次工具补丁因换行转义被拒，未写入；修正后成功。期间两条rg正则转义错误已改用精确检索，未据错误结果下结论。9项共享资源、语法、git diff --check通过。

两批cleanup-independent.json独立确认13128/18300/12784及27356/26008/12940全部退出，49755/51161无监听，Augit-daily-refresh-YDTdjZ及Augit-daily-refresh-cN1RQm已删除。分析会话已结束，单测临时目录由finally回收。原始PNG、JSON、录像为正式证据；历史参考清理审批阻塞及Nahimic权限阻塞保留。没有提交、暂存或覆盖既有混合修改。

## 下一步和启停

优先核对模态初始显示时边缘已有像素的来源与DWM/业务子窗呈现边界，而非继续将灰边变化当作后来新增的绘制。需要源图、系统图与原生裁切身份共同支持候选；不以纯灰边代替PyCharm系统阴影验收。正常启动设置慢开、无CDP完整开关、背景透明帧、55页全模态、终端与完整阴影仍未完成。复杂Git/冲突精修暂停，移除三栏方案尚未确认；Windows10排除本轮，跨屏DPI未验证，整体goal保持active。

启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1

停止优先正常关窗；异常仅对本次核实PID执行 taskkill /PID <本次PID> /T /F。

复验：node tools/audit/refresh-daily-file.cjs C:/Users/wl024/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright artifacts/pixel-followup-2026-10-17/<新目录> --git-fixture --trace-edge-source --gdi-clock

分析：D:/Python/Python3119/python.exe -B tools/audit/compare-modal-edge-source.py <新证据目录>
