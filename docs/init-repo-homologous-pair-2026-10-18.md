# 初始化仓库同态配对（2026-10-18）

## 1. 结论

在隔离的非 Git 目录取得两端“创建 Git 仓库”入口，登记为**页面级同态状态配对（已知差异，非像素合格）**：

- 相同：同一非 Git 目录 `init-fixture`；无 `.git`、无引用/提交；两端都提供创建仓库入口；均未执行初始化、未写文件。
- 实际：居中 `Create Git Repository` 模态；`Destination D:\AugitAcceptance\init-fixture`；`Choose Directory...`；说明“Initialization creates .git metadata in this directory; existing files are kept.”；`Cancel`/`Create`。
- 参考：底部 `Version Control` 工具窗空态；`To track changes to code:` + `Share project on GitHub or GitLab` / `Create Git repository...` / `More VCS plugins...` / `Version control integration`；无模态。
- 页面级同态配对 **43 → 44**；像素合格仍 **0**。

## 2. 取证路径

- 临时目录 `D:/AugitAcceptance/init-fixture`（非 Git；取证后删除）。
- 实际 PID 22996：正常入口打开该目录，自动弹出 `Create Git Repository`；Cancel/关闭，未初始化。
- 参考 PID 17060：临时预信任该路径后打开；Alt+9 打开 `Version Control` 工具窗，显示空态创建入口；未初始化。
- 取证后 `trusted-paths.xml` 恢复 `6E40F668…D2C9`；实际 `settings.json` 恢复 `B8868855…DC3D`；临时目录删除。

## 3. 已知差异与边界

- 实际是带目标路径的模态；参考是工具窗空态 + 链接。
- 未测：执行初始化后的仓库状态、Choose Directory、已存在 `.git`、深色。
- 两端的“创建”都未执行，不能据此宣称初始化流程已验收。

## 4. 证据

- 实际：`artifacts/pixel-followup-2026-10-18/init-repo-pair/actual/actual-init-repo2.png`（`8190AF95…1223`）。
- 参考：`reference/reference-init-repo3.png`（`5BE87424…215B`）。

## 5. 清理

- 实际 PID 22996、参考 PID 17060 已结束；临时目录删除；`trusted-paths.xml` 恢复 `6E40F668…D2C9`；`settings.json` 恢复 `B8868855…DC3D`；`cleanup-check-134.json`；未提交、未推送。
