# Augit 开发规范

本文件只规定通用开发过程。产品行为以 `docs/product-spec.md` 为准，技术方案以 `docs/architecture.md` 为准，界面结构和视觉规则以 `docs/ux-spec.md` 及 `docs/design-system.md` 为准。`docs/roadmap.md` 只保留历史实施记录，不作为当前设计、实现约束或冲突判断依据。架构不得反向改变产品行为；上述当前规范出现冲突时必须停止实施并向用户确认，不得自行选择一种解释。

- 严禁盲目执行指令。修改前必须核对产品规格、架构边界、影响范围和已有改动。
- 开始修改前，按 `docs/development-validation.md` §1 的顺序读取项目规范、当前模块文档、工作区状态和权威参考。
- 当前产品支持 Windows 10 22H2 x64 和 Windows 11 x64；没有对应实机条件时不得宣称已完成该平台验证。
- 普通文件查看器不提供编辑和保存。用户主动执行 Git 操作时，允许本机 Git 修改工作区；三栏冲突解决器的结果区是 Augit 唯一允许用户直接编辑并保存文本的地方。
- Augit 不接入 AI。不得覆盖、删除或回滚用户或其他 agent 已有改动。
- 技术基线是 C# / .NET 10 原生 Win32 外壳 + WebView2，界面本体是 `web/` 下的 HTML、CSS 和 JavaScript。主界面不得重新引入 WPF、WinForms 或原生控件自绘正文。
- `docs/ux-mockups/` 既是设计基线也是运行时界面代码来源：`mockup.js`、`mockup.css`、`current-find.js`、`image-preview.js` 在 `docs/ux-mockups/` 与 `web/src/` 之间必须字节一致，改动后运行 `tools/audit/verify-ui-assets.ps1`。不得修改视觉稿以迁就实现。
- 内存测量必须按父进程关系归属到本实例，不得把其他应用的 WebView2 进程计入。未经用户确认不得切换技术栈。
- 文档和代码注释使用中文，文件统一使用 UTF-8 和 LF。
- 新增或修改可执行逻辑必须补充自动化测试，并执行格式检查、相关测试和与风险相称的验证。
- 测试必须按修改范围分批执行；小批次只跑受影响测试，大模块完成或公共契约变化后再跑相应完整验证，严禁高频运行全量测试。具体命令和触发条件见 `docs/development-validation.md`。
- 测试开启的临时服务器、后台进程和创建的临时文件必须在测试结束后关闭、释放或删除。
- 新增依赖必须说明用途、运行时成本、替代方案、许可证和移除条件，并锁定版本。
- Git 提交严格遵循 Conventional Commits；每个大模块形成可复验结果后再单独提交。
