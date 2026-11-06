# 便携版自动更新

## 目标

Augit 只发布 self-contained 便携 zip，不提供安装器。便携版必须能在应用内完成：

1. 检查 GitHub Release 是否有更新；
2. 显示版本和非阻塞提示；
3. 下载便携 zip；
4. 校验签名、大小和 SHA-256；
5. 用户确认后重启并替换当前程序目录；
6. 替换失败时回滚并重启旧版本。

不静默安装，不在用户正在运行终端或 Git 操作时强制替换。

## 发布清单

每个 Release 除便携 zip 外还上传：

- `update.json`
- `update.json.sig`

`update.json` 的字段：

```json
{
  "schema": 1,
  "version": "1.0.0-rc.12",
  "tag": "v1.0.0-rc.12",
  "asset": "Augit-1.0.0-rc.12-win-x64-portable.zip",
  "url": "https://github.com/Paranoid49/Augit/releases/download/v1.0.0-rc.12/Augit-1.0.0-rc.12-win-x64-portable.zip",
  "size": 39258988,
  "sha256": "<64 hex>",
  "notesUrl": "https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.12",
  "publishedAt": "2026-11-06T03:00:00.0000000Z"
}
```

签名覆盖 `update.json` 的**原始 UTF-8 字节**。`update.json.sig` 是 Base64 编码的 ECDSA P-256 签名（P1363 r||s 格式）。客户端内置公钥位于
`src/Augit.Infrastructure/Updates/UpdateManifest.cs` 的 `UpdateTrust.PublicKeyBase64`。

签名私钥只保存为 GitHub Actions Secret `AUGIT_UPDATE_SIGNING_KEY`，格式为 PKCS#8 的 Base64；不得提交到仓库、日志或发布说明。

## 检查与下载

`UpdateService`：

1. 请求 GitHub Releases API；
2. 过滤 draft，按 SemVer 选择最高版本（当前 rc 阶段包含 Pre-release）；
3. 下载该 Release 的 `update.json` 和 `update.json.sig`；
4. 先验签，再解析和校验 tag、版本、GitHub HTTPS 地址、大小和 SHA-256；
5. 只有版本高于当前版本才提示。

下载时把便携 zip 写到：

```text
%LOCALAPPDATA%\Augit\updates\<version>\
```

并校验清单中的大小与 SHA-256。解压只接受根目录内路径，校验包内 `Augit.exe` 的 ProductVersion 与清单一致后才允许应用。

网页只能调用 `update/check`、`update/download`、`update/apply`；下载接口不接受网页提交的 URL 或清单，避免绕过验签。

## 替换与回滚

Windows 不允许运行中的 `Augit.exe` 覆盖自身，因此下载完成后：

1. 主程序启动暂存目录中的**新版本** `Augit.exe --apply-update --parent-pid <pid> --target <便携目录>`；
2. 新进程进入更新模式，不初始化 WebView2；
3. 等待旧进程退出；
4. 把旧目录重命名为同卷备份 `Augit.update-backup-<timestamp>`；
5. 复制暂存目录到原目录；
6. 启动新的 `Augit.exe`；
7. 等待新窗口出现；进程退出或 10 秒内没有窗口时恢复备份并重启旧版本；
8. 正常启动时清理成功更新留下的备份目录。

目标便携目录必须可写。如果程序放在 `Program Files`、只读盘或受策略保护的目录，更新会失败并尝试恢复旧版本；用户应放在用户可写的普通目录。还有同目录 Augit 窗口在运行时，更新会被拒绝，必须先关闭全部窗口。

设置和会话保存在 `%LOCALAPPDATA%\Augit`，不随程序目录替换而丢失。应用目录不应存放用户文件。

## 用户体验

启动完成后后台检查一次，不阻塞首屏。发现更新时显示通知：

```text
Augit 1.0.0-rc.12 is available
You are using 1.0.0-rc.12. Download the update, then restart Augit to install it.
[Download update]
```

下载完成后显示：

```text
Update ready
Augit 1.0.0-rc.12 is ready. Restarting will close the current window and its terminal sessions.
[Restart and update]
```

更新不会自动杀掉正在运行的终端或 Git 操作；用户必须点击重启动作并自行确认终端会话会被关闭。

离线、GitHub 超时、签名失败、哈希不匹配或暂存失败都只显示错误通知，不改变当前程序目录。

## 发布签名

发布流水线使用：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass `
  -File tools/update/create-release-manifest.ps1 `
  -Version <版本> `
  -Tag v<版本> `
  -ZipPath <便携 zip> `
  -OutputDirectory <发布目录>
```

脚本从 `AUGIT_UPDATE_SIGNING_KEY` 读取私钥并生成两个签名资产。缺少 Secret 时发布失败，不能发布未签名清单。

首次配置：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/update/generate-signing-key.ps1
```

把生成的私钥 Base64 写入 GitHub 仓库 Secret `AUGIT_UPDATE_SIGNING_KEY`；公钥写入 `UpdateTrust.PublicKeyBase64` 后重新构建。私钥另存于密码管理器，不进入仓库。

密钥轮换必须先发布一个同时信任旧/新公钥的过渡版本；当前客户端只内置一个公钥，不能在用户不知情时替换信任根。

## 边界

- 不做后台静默安装；必须用户确认重启。
- 不做增量更新，当前每次下载完整便携 zip（约 39 MB）。
- 依赖 GitHub Releases API 和下载链路；中国大陆网络可能需要代理，失败不阻塞应用。
- 仍需系统 WebView2 Runtime；它是界面运行依赖，不随 Augit 更新。
- 仍未做 Authenticode 代码签名，SmartScreen 可能提示未知发布者；签名清单只保证更新来源和完整性，不等于 Windows 发布者信誉。
- Windows 10 22H2 实机与更新失败回滚仍需真实验证。
