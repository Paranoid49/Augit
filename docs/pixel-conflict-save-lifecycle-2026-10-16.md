# 冲突保存退出与整数客户区接续

日期：2026-10-16。此次修复冲突保存成功后仍停留在结果编辑器的回归，并收束此前已写入工作区的整数客户区布局。整体四项目标未完成；设置独立WebView架构确认仍待用户答复，本批没有修改设置架构。Windows10排除本次验收，跨屏DPI保持未验证。

## 根因与改动

修前conflict-save-response-diagnostic/failure.json证明git/conflict-save请求93返回available=true、conflictCount=0，磁盘精确为edited first line、ours value、last line三行并保留尾部换行，索引未合并项为空。超时发生在随后等待结果编辑器消失，不是保存请求超时。此前“保存超时”的描述应按此更正。

applyConflictResult只清除dirty，没有结束conflictDraftSource指向当前文档的编辑会话；openConflictSession为了保护草稿而持续保留编辑器。与此同时，recheckOpenConflict没有冻结保存中的外部读取，自身保存触发的文件通知可能显示“文件已不是冲突，未保存内容不会写入”的误导提示。

本批成功保存复用returnToConflictList，释放当前文档、草稿、撤销引用、高亮和外部询问，再读取真实操作会话。失败、取消及异常仍保留正文和撤销记录。外部读取增加请求序号；保存开始使既有读取失效，保存中不发起读取，返回时核验当前live对象、文档身份、请求序号和保存状态。这样保存失败后才到达的旧读取也不能覆盖失败原因。

此前未收束的mockup.js/mockup.css整数布局继续保留：外窗物理尺寸不变，内部网格采用向下取整的逻辑客户区宽高，ResizeObserver同时观察外层dialog，避免仅内部固定网格观察遗漏分数尺寸变化。两份视觉稿与运行时共享文件保持一致。

## 验证与证据

- 新增conflict-save-lifecycle.test.cjs直接执行产品函数。修前11项中4项失败；修后连同草稿测试共20项通过，169.0462ms。覆盖成功释放、拒绝/取消/异常保留、重复提交、保存前后迟到读取、并发读取顺序及真实外部变化询问。首个测试版本因等待不应发出的读取而挂起，仅结束自己的Node父子进程13692/26428后修正测试；不将该次运行记为完成。
- 正常启动器PrepareOnly执行非增量Debug构建11.04秒、0警告0错误。真实WebView2取证conflict-save-lifecycle-fixed使用随包web，不覆盖web-root；隔离Git仓库、设置和profile，明确使用参考窗口尺寸，因此不是默认用户会话实测。接受/忽略、撤销重做、跨行选区、协议组合输入、真实外部改写保留、精确保存和索引解决通过。interaction.json确认view=list，文档/草稿/来源/待处理均null、applying=false、网页错误0。原生Windows输入法候选窗未验证。
- 新取footer-geometry.json与已保留参考运行时数据逐项比较：按钮x=12/117/772/898，y=652，宽99/108/120/78，高34。右组原约0.571463px、纵向约0.285723px偏差消除，最大浮点残差0.000052逻辑px。结果写入footer-reference-comparison.json。既有conflict-client-integer-layout/client-rounding.json有30组，上一阶段96组完整冲突矩阵已有通过记录，本批没有重复跑矩阵。
- 已查看initial.png，但它是CDP view图，不能作为系统阴影或整窗屏幕同态证明。后续conflict-integer-system-pair尝试完整三状态系统图，在指针移出检查点超时，退出1；未产出合格系统配对，不增加55页合格数。屏幕观察可见指针光晕，不能忽略或遮掉再宣称通过。
- Release非增量构建6.32秒、0警告0错误；Debug/Release各24项web镜像逐文件一致，共享9资产一致。产品和驱动语法、git diff --check、文档声明检查通过；历史全量快照仍过期，不作为本批证据。

证据根为artifacts/pixel-followup-2026-10-16。live-data.js的MD5由38A6F63C55532F4D67A210E5ED933F88变为AC578DFFFB854EC44B2FB8D6E7266B91。mockup.js=FF0DA3AD5B3300D01716EFBE618AA590、mockup.css=FA339DDEBB91AEE32BFCB2600701B046、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9保持本批开始值；前两项包含此前整数布局。本批未回滚混合工作区、暂存或提交。

## 资源与复验

真实实例11608与24484及其子树由驱动finally结束，端口57285/60521无监听，隔离目录Augit-merge-pair-x4VDPw/h885Jg均独立确认不存在。取证检查点由驱动移除；没有启动PyCharm或服务器，没有触碰历史被拒绝清理目录。

```powershell
# 日常启动，自动构建；停止优先正常关闭窗口。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 异常时只停止本次打印的PID。
taskkill /PID <本次PID> /T /F
# 只在相关逻辑修改后执行定向回归。
node --test tools/audit/conflict-save-lifecycle.test.cjs tools/audit/conflict-draft.test.cjs
node tools/audit/capture-conflict-reference-shell.cjs <新证据目录> <Playwright模块路径> --light --reference-frame --pair-focus --bundled-web
```

后续需取得无遮挡的初始/接受左侧/全部处理系统原图，对照参考完整像素；整数几何相同不证明字形栅格和系统阴影一致。设置闪烁、55页全部模态、终端逐行和阴影量化均保留，不能把本批成功保存与底栏几何当成整体完成。
