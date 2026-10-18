# 跳转行无遮挡空输入双端取证（2026-10-16）

## 本批推进

回到55页中的go-to-line，用PyCharm2026.2.1和当前Debug随包web的正常入口重新取得空输入、输入聚焦的系统原始PNG。两端均584×252、DPI168，前后前景／进程／窗口边界稳定，指针在采样区外96物理px以上；两张原图已目视确认无遮挡。参考通过Ctrl+G后清除选中的预填值，未改文档；Augit从普通入口Ctrl+G打开，未注入场景或覆盖CSS。

这补足此前标题栏修改后截图含指针光晕的问题，没有增加全页合格数。光标闪烁相位仍不同；品牌、标题和标签保留已授权的仅行号功能差异；两端父窗面板不同且字体回退尚未逐角色取证。qualifiedPair及pixelRestorationVerified仍false，不能把本批样本当作全部模态验收。

## 原始像素差异

证据目录artifacts/pixel-followup-2026-10-16/goto-empty-state-pair，reference-empty.png与actual-empty-strict.png均不缩放、不作像素平移。comparison.json保存哈希、固定ROI、纯色范围和限制；side-by-side.png仅原尺寸并排，difference.png保存绝对RGB差。

| 区域 | 像素总数 | 不同像素 | 超过RGB2 |
| --- | ---: | ---: | ---: |
| 整窗 | 147168 | 7452 | 7331 |
| 输入ROI[190,85,562,160] | 27900 | 1412 | 1368 |
| 按钮ROI[275,175,565,240] | 18850 | 1904 | 1830 |

各ROI排除0像素。整窗包含已授权文案及品牌差异，不能把这些数解释为还原率。输入框纯品牌蓝范围为参考[204,95,546,147]、实际[201,95,543,147]，两者同宽高但实际横向早3物理px；参考／实际OK纯色范围[286,184,411,232]／[285,184,411,233]，Cancel边框[433,184,558,232]／[432,184,558,233]。纯色范围不等于控件外接矩形，也不包含全部抗锯齿边缘。

参考SHA256 a1348e76b40cc36d1bc97300fc2fba849c891a44e21f1f7acf661032f35f0a5c；实际df03f9e021f1acc44b7bdde1d947e25991c057bc69a83a1bad04c42de9dd333c。

## 新增定位依据

使用已有ReferenceMergeGeometry只读探针，Java17编译与自检通过；第二个独立参考进程取得reference-transform.json及对应reference-transform-prefilled.png。输入组件仍200×34、Insets3、相对客户区逻辑位置113／52；客户区333×144。实际组件Graphics变换为[1.75,0,0.75;0,1.75,0]，而DialogRootPane为[1.75,0,0;0,1.75,0]。这说明整数screen坐标不能直接换算边框原始像素；component.getGraphics不是截获真实paint调用，因此不把0.75相位直接认定为全部3px偏差根因。

本地DarculaTextBorderNew先扣Insets，再由DarculaNewUIUtil按focusWidth扩展圆角环。当前逻辑控件尺寸一致，必须进一步核对原生客户区物理原点及实际paint变换后才能修正；本批没有增加无来源平移或改产品字号。

## 验证与清理

真实Augit11项定向行为通过，包括默认尺寸、按钮顺序、键盘正反循环、非法输入、跳转、焦点恢复及取消；错误列表为空。未改产品代码，未重复全量测试或构建。git diff --check通过。

Augit13624及其子进程由finally退出，端口55055无监听，Augit-goto-pair-kTggjq已删除。参考5940及6740子进程均已停止，结束时PID复查无残留。本批停止6740并删除代理临时目录的组合命令被自动审批以blocked by policy拒绝；随后单独停止成功，未重试删除。临时目录C:/Users/wl024/AppData/Local/Temp/Augit-goto-transform-810d4562535644abb6172ea7a6fd74f7保留，不能宣称全部临时文件已清理。旧拒绝清理目标未触碰。

未暂存、提交或回滚工作区混合修改。整体四项目标继续未完成；Windows10按用户要求排除，跨屏DPI未验证。

```powershell
# 日常启动；正常关闭窗口即停止。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
# 定向取证，按提示观察／激活实际窗口后释放检查点；finally自动清理。
node tools/audit/capture-goto-line-shell.cjs <Playwright路径> <新证据目录> --activate-main --wait-foreground
# 异常时只停止本次启动输出PID。
taskkill /PID <本次PID> /T /F
```
