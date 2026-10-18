# 设置背景刷新：事件消费与取证干扰（2026-10-16）

## 结论

本批修复真实文件变化被推送与两秒兜底重复应用的问题，并定位上一批组合分类检查失败的取证干扰。仍未解决原生边缘闪烁或证明整表面无闪；全55页/全模态、终端及冲突逐行、完整阴影仍未完成。Windows10排除本次实机验收，跨屏DPI保持未验证。

上一轮属于有效进展：脱离节点误刷新已在产品中修复，真实探针由448.5ms流程变为零请求。本轮接续未将该局部成果视为整个目标完成。

## 根因证据

`settings-background-origins/navigation.json`记录宿主workspace-changed明确包含本次取证写入的detached-mutations.json及其目录。随后applyWorkspaceChanges先后从推送及drainWorkspaceChanges触发背景重绘。1081条背景节点变更、4个捕获请求使分类门禁失败。由此确认不是分类导航自身必然触发，而是测试在被监听仓库内提前落盘。此前两次组合失败保留，不删除证据或放宽分类断言。

真实产品还有独立问题：ShellBridge将文件/Git批次留在待消费集合，同时推送通知；网页直接应用推送载荷，兜底随后再次读取同一批次并重绘。新增测试修前普通文件/Git两项均失败，实际应用2次而非1次；丢推送兜底测试已通过。

## 修改

- live-data.js收到workspace-changed后立即调用既有drainWorkspaceChanges，统一经宿主workspace/changes原子取走。没有按路径或时间窗口去重，同路径后续真实写入仍更新；推送丢失仍由兜底消费。桥接协议、C#队列、业务能力及背景实时性不变。
- check-settings-close.cjs将JSON先留内存，实例结束后再写证据，防止自己刺激文件监听；可选记录宿主事件及渲染调用栈。新增真实文件连续两次修改探针，各跨越2300ms兜底周期。
- live-shell.spec.cjs的模拟宿主推送同步进入原有__nextChanges队列，与真实宿主一致；新增五项定向集成模块。没有运行全量套件。

## 验证

20项JS定向通过，197.22ms。新增5项浏览器集成覆盖立即消费、兜底不重放、同路径再次变化、漏推送恢复及网页错误；51项冲突模块回归通过。既有ShellBridgeWorkspaceChangesTests宿主合并/消费测试1项通过（约2秒），未改C#可执行代码。

真实Debug用普通场景入口、随包网页、本仓库9标签、默认配置副本及隔离profile。settings-queue-final中外部文件两次修改各只消费1次；脱离节点探针零请求；随后四分类背景变更0、模态呈现/捕获请求0。重复入口、延迟呈现及遮盖、嵌套返回通过，网页错误0，最终快照0、所有者恢复。五条关闭路径OK/Cancel/Esc/标题关闭/Apply后OK为172.23/149.75/138.90/144.09/180.26ms。该单次测量不能证明所有操作都达标，也不能证明无闪帧；本批未重复已知失败的系统录像。

Debug/Release构建0警告0错误，2.66/2.92秒；两套各24项web镜像一致，共享9资产及语法、git diff --check通过。Release本批只构建和镜像验证，没有再跑实机。

live-data.js MD5由F03F2393B7E03DF3B6BFFEEEBBF25987变为AD3BA8F7851F463ABF99FF932B4CA95A。mockup.js=396E779B261AC31FBE4B625181134680、mockup.css=15DE14CA11BB3E8682E403FB7EF5E054、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9均保持。混合工作区未暂存、提交或回滚。

## 清理与命令

本批Augit2056/20392及所属进程由驱动结束，64577/53694无监听；临时目录Augit-settings-close-U7lvWY/kjYYTd不存在，watcher-probe.tmp已删除。浏览器专项由原驱动finally关闭；原始JSON作为证据保留。

```powershell
# 正常启动；停止时正常关闭窗口。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 异常时仅针对本次启动输出的PID。
taskkill /PID <本次PID> /T /F
# 实机组合回归；本驱动finally负责关闭实例与清理临时目录。
node tools/audit/check-settings-close.cjs <Playwright模块路径> <新证据目录> --many-tabs --delay-cover --delay-present --nested --bundled-web --navigation --detached-mutations --trace-background-origins --external-change
# 定向队列与冲突模块；设置AUGIT_BROWSER_EXECUTABLE为本机Edge路径。
node tools/audit/live-shell.spec.cjs <Playwright模块路径> --workspace-delivery-only
node tools/audit/live-shell.spec.cjs <Playwright模块路径> --goal-conflict-only
```

下一步仍须消除真实背景变化时的原生遮盖交接闪帧。减少多余捕获降低触发频率，不能替代整表面呈现验收。
