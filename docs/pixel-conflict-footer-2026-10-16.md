# 冲突底栏字重与三状态补采

## 2026-10-16 按钮宽度接续

继续定位上一批反证，发现真实参考组件的按钮宽99/108/120/78，浏览器原实现99/108/121/78。独立运行安装版JBR及Microsoft YaHei UI12，FontMetrics.stringWidth对Apply Changes返回86，浮点bounds约86.285713（整数度量）或86.349609（小数度量）；浏览器Canvas约86.349609，却使用ceil得87，再加34形成121。参考getPreferredSize按FontMetrics返回的整数布局。

本批仅把measureConflictResolver底栏文本宽度ceil改为round，加34及78下限不变；没有硬编码四个按钮宽度或增加任意位置偏移。规格同步这一整数度量规则。新增UI12基线覆盖到已有矩阵，96组一次通过（原72组增加24组）；仍检查UI13/40、代码字号、浅深、DPI、首行空侧及实际半粗文本边界。

一次真实WebView2取证conflict-footer-width-live确认按钮宽99/108/120/78、高34，Apply字重600，其余400。新增footer-geometry.json以及footer-reference-comparison.json：左组x与参考12/117吻合，右组x仍比参考772/898多0.571463逻辑px，全部y多0.285723逻辑px。当前1730×1215物理客户区在WebView2为988.571×694.286逻辑px，参考整数布局988×694，数值与剩余偏差一致；这只是原因定位，尚未实现整数客户区绘制语义，不能宣称位置或底栏像素通过。未重复系统补拍，鼠标光晕问题仍待解决。

独立JBR源码ButtonFontProbe.java保留在conflict-footer-screen，输出保存于conflict-footer-width-live/jbr-button-widths.txt。源码仅测量字体，不冒充实际PyCharm组件；实际组件宽度证据仍来自reference-runtime-geometry.json。

本批mockup.js MD5由A8167A59D3CBFD452683C82FA7A44D61变为44D6C53684B74DBC6116FF25F7C22932；CSS保持D5DADE8F4DB84506031A9AB3B1F5DB80，其余两项沿下文。共享9资产、Debug/Release各24文件镜像、相关JS语法与git diff --check通过；无C#改动、无构建或全量回归。PID24340、端口50847及Augit-merge-pair-Dvdq76已清理并独立复查。浏览器布局及诊断实例均finally退出，证据源码/输出保留为交付记录。

复验使用tools/verify-ux-conflict.cjs及capture-conflict-reference-shell.cjs的--light --reference-frame --capture-only，输出另选新目录。手动产品启停沿本文末尾；整体四项目标继续保留，55页合格数不变。

日期2026-10-16。上一轮禁用图标修复属于实际进展，本批继续完整截图定位；未改动业务逻辑、暂存、提交或回滚既有修改。

## 发现与整改

当前系统图与已保留PyCharm2026.2.1参考的底栏对照，Apply Changes参考为强调字重，实际为400。参考DarculaButtonUI.setupDefaultButton对Windows默认动作在绘制时使用Font.BOLD，getPreferredSize仍取组件普通字体。依据现行仅400/600规则，本批仅为conflict-save-actions的primary-button设置600，保持普通字体首选宽度计算；不修改其他页面按钮。设计系统同步，视觉稿、正常入口、Debug及Release随包CSS一致。

72组定向检查一次通过，新增Apply为600、Cancel为400，并沿已有实际文字边界断言覆盖大字号裁切。没有重复全量测试或C#构建。工具栏禁用/启用既有状态测试一并通过。

## 原始证据与反证

修前conflict-toolbar-screen三状态系统图1730×1215、DPI168，前景/PID/几何检查通过，但初始图左上角有鼠标标记。修后conflict-footer-screen改在已观察的主窗右下停放指针，实际图右下仍出现光晕。两批不登记无遮挡整窗通过，不增加55页合格计数；不能由目录名或前景门禁推断没有遮挡。

修后三状态完整不同像素87307、93844、103523，正文25193、25844、29136，来源标题分隔线均0；底栏18633、24519、30813，受光晕影响，不用此计数宣称修复收益。独立未遮挡应用按钮区域[1345,1135,1565,1205]未平移未重采样比较：不同像素2419→2651，RGB绝对差476094→539949，原始像素指标反而增加。局部放大目视确认字重现与参考方向一致，但按钮横向位置、宽度及字形栅格仍不同；没有以对齐裁剪或过滤消除此反证。本批只结案默认动作字重，底栏像素对齐未结案。

原始图片/侧车、pair-states及layout位于artifacts/pixel-followup-2026-10-16/conflict-toolbar-screen与conflict-footer-screen。primary-button-comparison.json保存原始区域计数，primary-button-review.png仅为放大审阅图，不作原始配对证据。

## 校验与清理

9项共享资产、Debug/Release各24项资源镜像、JS语法及git diff --check通过。CSS MD5从06EA2451420B1F14DB2A16DB3BAD7AA9变为D5DADE8F4DB84506031A9AB3B1F5DB80；mockup.js=A8167A59D3CBFD452683C82FA7A44D61、live-data.js=C8DA64065C77E60234F2D614334E8DC4、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9保持。

本批PID1912/24756退出，端口62370/59703无监听；临时Augit-merge-pair-B1q0XI/OieexY已由finally删除并复查不存在。没有启动参考进程，不触碰历史被拒清理目录。

手动启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。正常关闭窗口；异常停止仅用本次PID：taskkill /PID <本次PID> /T /F。系统配对用capture-conflict-reference-shell.cjs的--light --screen-pair --reference-frame --pair-focus --activate-main；前景检查点需实际观察激活后才放行。

整体55页及全部模态、背景刷新、终端/冲突逐行、系统阴影目标仍未完成。下一步须解决无遮挡取证与按钮实际位置/字形差异，不将本批字重修复扩大为底栏通过。
