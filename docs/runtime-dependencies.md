# 运行时依赖锁定基线

本文记录安装版联网安装运行时所使用的锁定基线，统一维护版本、微软官方来源和完整性信息。

校验日期为 2026-08-27。任何版本或下载地址变更都必须同步更新完整性信息并重新执行启动、预览和发布验证。

## .NET Runtime

- 产品：.NET 10 Runtime，Windows x64。
- 锁定版本：`10.0.11`。
- 官方发布元数据：`https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json`。
- 固定下载地址：`https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.11/dotnet-runtime-10.0.11-win-x64.exe`。
- SHA-512：`694e0e0af26b2b8949b8eda8a3831ab31aeac79797d43d6ff8c8798eae642c0904852e641c47329d7d893408f25feab1530ca2b7a0c6ed0d991e0113466a4bf9`。
- 校验规则：下载完成后先校验 SHA-512，再校验有效的 Microsoft Corporation Authenticode 签名，任一失败都不得执行安装。

选择 `10.0.11` 是因为它是校验日 .NET 10 通道的最新安全修复版。原生 Win32 外壳只依赖基础 .NET Runtime，不安装更重的 Desktop Runtime。项目 SDK 仍按 `global.json` 独立锁定，运行时补丁版本不改变目标框架或技术栈。

## Microsoft Edge WebView2 Runtime

- 产品：Microsoft Edge WebView2 Evergreen Standalone Installer，Windows x64。
- 兼容基线：`151.0.4129.107`。
- 官方下载页：`https://developer.microsoft.com/microsoft-edge/webview2/`。
- 微软官方 x64 转发地址：`https://go.microsoft.com/fwlink/?linkid=2124701`。
- 校验日解析出的固定下载地址：`https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/89620190-81af-46a2-bb59-6228918a312e/MicrosoftEdgeWebView2RuntimeInstallerX64.exe`。
- 文件大小：`213044432` 字节。
- SHA-256：`358A11CFF88CE519301C3B60BCEFE848F922688AB0A333FC0F18DDF83BB3B4F3`。
- 安装器文件版本：`1.3.263.3`。
- Authenticode 签名者：`Microsoft Corporation`，校验状态为有效。
- 签名证书指纹：`4028CAD637509D4744B17EC5B42AED8D7A31E6AF`。
- 校验规则：阶段五安装器必须使用本文固定地址，依次校验文件大小、SHA-256 和有效的 Microsoft Corporation Authenticode 签名，任一失败都不得执行安装。

WebView2 SDK 版本继续由 `Directory.Packages.props` 锁定为 `1.0.3650.58`。普通启动不创建 WebView2；只有用户打开 Markdown 预览或内置终端时才检查并加载运行时。
