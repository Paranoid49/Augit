# 安全策略

## 支持的版本

当前只维护 `1.0.0-rc.2` 预发布候选的公开资产。旧预发布包不再单独修复，遇到安全问题请升级到最新 Release。

## 报告安全问题

请不要在公开 Issue 中提交可利用细节、凭据、私有仓库内容或未脱敏日志。优先使用 GitHub 的 [Private vulnerability reporting](https://github.com/Paranoid49/Augit/security/advisories/new) 提交；如果该入口不可用，请通过仓库维护者公开资料中的联系方式联系，并在标题中注明 `SECURITY`。

报告时请附上：

- Augit 版本（ProductVersion）和安装/便携方式；
- Windows 版本与 WebView2 版本；
- 可复现的最小步骤和影响；
- 是否需要本机 Git 凭据或特定仓库配置。

## 已知边界

- Augit 当前未做自身代码签名，SmartScreen 可能显示「未知发布者」；这不等于安装包被篡改，下载后请核对 `SHA256SUMS.txt`。
- Augit 不保存凭据，Git 认证由本机 Git Credential Manager、SSH agent、代理和终端环境负责。
- 安装器只从项目锁定的微软官方地址下载 .NET/WebView2 运行时，并校验固定哈希与 Microsoft Corporation 签名。
- Augit 不做遥测、崩溃上报或自动更新。

## 响应

维护者会在确认问题后评估影响范围、修复版本和披露方式。正式 1.0 之前，安全修复随新的 `1.0.0-rc.N` 预发布包提供。
