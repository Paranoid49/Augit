# Augit 1.0.0-rc.17 公开发布清单

本清单记录 `1.0.0-rc.17` 公开预发布资产的构建方式、发布后核对结果和验证边界。Release 页面与 `SHA256SUMS.txt` 是资产哈希的权威来源；本文件在流水线完成后补入实际资产、构建提交和发布 id。

## 1. 发布身份

| 项 | 值 |
| --- | --- |
| 发布页 | <https://github.com/Paranoid49/Augit/releases/tag/v1.0.0-rc.17> |
| Release id | `461599232` |
| Tag | `v1.0.0-rc.17` |
| 构建方式 | GitHub Actions `.github/workflows/release.yml`，从 tag 指向的提交构建（run `46152171712`，2026-11-28） |
| 构建提交 | `25d211fc187abcddc00989735acedc1ab1dfe7c8` |
| 版本 | `1.0.0-rc.17` |
| 形态 | **self-contained 便携 zip**（自带 .NET 10.0.11 Runtime） |
| 自动更新 | `update.json` + `update.json.sig`，ECDSA P-256 |
| 安装器 | **不提供**；发布单一便携 zip |
| FileVersion | `1.0.0.0` |
| 代码签名 | **NotSigned**（无 Authenticode；更新清单有独立签名） |

rc.17 修复项目树、Changes 和 Git 日志右键未选中行时旧高亮不更新的问题。

## 2. 资产

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Augit-1.0.0-rc.17-win-x64-portable.zip` | 39,299,152 B | `c6f5cb3da2860ad5a8840e541c3ec94a5d92fd04b409ae064d7bcfd7a2ae874b` |
| `update.json` | 432 B | `6a5ffda7f1fb13ded289994d21f4c421509aa80ca31f274c7b5bd3ca3055e464` |
| `update.json.sig` | 89 B | `b5b6d090375b19e1a8e18cf0386c766d36e2b879c0de2dcaa99751c0cbb93300` |
| `SHA256SUMS.txt` | 265 B | `6dd126b7985c5f74031a49056755a9e0962943a0610387008840d3c98e378c8a` |

## 3. 构建与验证

发布流水线执行标准 rc.17 Release 构建、全部单元测试、便携打包、签名清单和发布上传。本机发布前门禁记录：

| 项 | 结果 |
| --- | --- |
| Release 构建 | **PASS**：0 警告 / 0 错误 |
| Core/Shell/Infrastructure 测试 | **PASS**：Core 77/77、Shell 286/286、Infrastructure 221/221，合计 584/584（排除 4 条 ConPTY，与 CI 口径一致） |
| UI 资产 / 脚本编码 / 文档声明 / `dotnet format` / `git diff --check` | **PASS** |
| rc.17 右键选中定向 live-shell | **3/3 PASS**：项目树、Changes、Git 日志的 DOM 高亮、状态字段和菜单目标一致 |
| 本地 self-contained 便携包 | **PASS**：`39,299,170 B`，SHA-256 `e2394ff35414fe468674c6da489bf69eec5d950189b758fdf730aac9801644ce`；ProductVersion `1.0.0-rc.17+934357a…`；窗口 313.9ms、首帧 594.9ms、关闭 145.6ms、退出码 0、无残留 |
| 全量 live-shell | 历史 harness 仍有已知过期选择器；本版不以它冒充全绿 |

## 4. 自动更新

- rc.12 起内置签名自动更新；用户已确认 `rc.12→rc.13`、`rc.13→rc.14`、`rc.14→rc.15` 的公开升级路径完成。
- **`rc.16→rc.17` 的公开升级端到端已完成（用户 2026-11-28 在公司 Windows 10 22H2 实机确认）**；`rc.15→rc.16` 仍未单独复验，保持待验证。本机公开 rc.15 正常入口启动时旧版 WebView2 环境未建立，不能把本机失败写成产品升级失败。
- 下载完整 zip 后校验签名、大小、SHA-256 和 `Augit.exe` ProductVersion；用户确认后由暂存的新版本执行替换，失败时回滚旧版本。
- 没有 Authenticode 代码签名，SmartScreen 仍可能提示未知发布者。

## 5. Windows 10 22H2 与用户确认

- 用户于 2026-11-28 确认在公司 Windows 10 22H2 实机连续运行多个 rc 版本，并确认 `rc.14 → rc.15` 公开升级完成。
- 用户于 2026-11-28 在公司 Windows 10 22H2 实机完成 `rc.16→rc.17` 公开升级；rc.17 启动与更新已进入真实使用。Windows 11 结果仍不替代 Windows 10 证据。

## 6. 发布后核对

2026-11-28 已完成：

- 发布流水线 run `46152171712` 全部步骤 success；Release 页面标记为 **Pre-release**（id `461599232`），四个资产匿名可下载；
- 匿名下载的 zip、`update.json`、`update.json.sig`、`SHA256SUMS.txt` 大小与 SHA-256 和发布正文一致；
- `update.json` 使用内置公钥验签 **PASS**；
- 官方便携包内容校验 **PASS**：必需文件、web 资源一致、无 PDB；`Augit.exe` 与三个托管程序集 ProductVersion `1.0.0-rc.17+25d211f…`，FileVersion `1.0.0.0`；
- 官方便携包真实启动 **PASS**：窗口出现 289ms、首帧 577.4ms、关闭 157ms、退出码 0、无残留子进程；
- **`rc.16→rc.17` 自动更新端到端：用户 2026-11-28 在公司 Windows 10 22H2 实机确认 PASS**；`rc.15→rc.16` 仍未单独复验。本机公开 rc.15 正常入口启动时旧版 WebView2 环境未建立，未进入更新界面，不能把本机失败写成产品路径失败；
- 全量 live-shell：**未通过**，原因是历史 harness 过期选择器；rc.17 定向块 3/3 通过，缺口保留。

状态：**PASS（预发布资产、官方启动、签名与 rc.16→rc.17 Windows 10 实机升级）**；`rc.15→rc.16` 升级和全量 live-shell 历史基线仍待复验。
