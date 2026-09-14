# Augit

Augit 是面向外部 AI 协作开发场景的轻量 Windows 只读项目查看器与 Git 图形工具。Augit 不接入 AI，也不提供普通代码编辑和保存；只有三栏冲突解决器的最终结果区允许直接编辑文本。

## 系统要求

- Windows 10 22H2 x64 或 Windows 11 x64。
- .NET 10 Runtime，不需要 .NET Desktop Runtime；安装器当前锁定并校验安全修复版 `10.0.11`。
- Microsoft Edge WebView2 Runtime；安装器当前使用兼容基线 `151.0.4129.107`。
- 使用 Git 功能时需要 Git for Windows `2.40` 或更高版本；Augit 不捆绑 Git。

安装版会检测前两项运行时。存在缺失项时，安装器先列出缺失内容并征得确认，再从微软锁定地址下载、校验哈希与微软数字签名并安装。便携版不会安装运行时，只会提示缺失项。

## 安装、升级与卸载

- 安装版需要管理员权限，默认安装到系统 Program Files。安装过程中可以选择加入系统 PATH，以及增加资源管理器“使用 Augit 打开”。
- 缺少运行时时，确认页会先列出待下载项目；用户拒绝或取消下载后，Augit 不会安装。
- 覆盖升级前先关闭 Augit，再运行新版本安装器并安装到原目录。安装器会沿用上次选择的 PATH 和资源管理器任务。
- 卸载时从 Windows“设置 > 应用 > 已安装的应用”中找到 Augit 并执行卸载。卸载器会删除程序文件、系统 PATH 项和资源管理器入口，不会删除工作区内容、Git 配置或 `%LOCALAPPDATA%\Augit` 中的个人设置。
- 便携版解压后直接运行 `Augit.exe`，不会写入系统 PATH 或资源管理器菜单。缺少 WebView2 时，Markdown 或终端会提供微软官方下载入口；缺少 .NET 运行时时，由 .NET 应用宿主显示缺失版本和微软下载入口。

## 打开目录

- 从界面选择“打开目录”或“最近目录”。
- 在命令行使用 `augit .` 打开当前目录。
- 使用 `augit <目录>` 打开指定目录。
- 安装时可选加入 PATH，并可选增加资源管理器“使用 Augit 打开”。

同一目录只保留一个 Augit 窗口；再次打开时会激活已有窗口并退出新进程。

## 内置终端

内置终端按需创建，首次默认使用 Windows PowerShell。可在设置中选择 PowerShell 7、CMD、Git Bash、WSL 或自定义启动命令。配置失效时会明确报错，不会静默切换 Shell。终端最多保留最近 2000 行，关闭存在前台命令的会话前会要求确认，并在确认后结束整个终端进程树。

## 明确边界

- 普通文件、Markdown、JSON、diff、历史和 blame 均为只读。
- 图片预览只支持 PNG、JPEG/JPG 和 BMP，不支持 GIF、WebP。
- 不提供 Force Push、Git 子模块、GitHub/GitLab 平台接口、多个 Changelist、Git Console、代码补全、调试器、插件市场或 AI 集成。
- 不做遥测、崩溃上报、自动更新检查或常驻服务。

## 从源码构建

```powershell
dotnet restore Augit.slnx --locked-mode
dotnet build Augit.slnx -c Release --no-restore
dotnet test Augit.slnx -c Release --no-build --no-restore
powershell -NoProfile -File .\tools\release.ps1
```

发布脚本生成 `win-x64`、非 self-contained 的便携压缩包和小型联网安装器，并在结束前删除发布暂存目录。

## 文档

- [产品规格](docs/product-spec.md)：产品行为与功能边界。
- [架构](docs/architecture.md)：技术方案与模块划分。
- [界面规范](docs/ux-spec.md) 与 [设计系统](docs/design-system.md)：界面结构与视觉规则。
- [运行时依赖](docs/runtime-dependencies.md)：锁定的运行时版本与来源。
- [第三方依赖说明](THIRD-PARTY-NOTICES.md)：直接依赖的用途、成本、许可证与移除条件。

## 许可证

本项目以 [MIT 许可证](LICENSE) 发布。第三方依赖的许可证与分发要求见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
