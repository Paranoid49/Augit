# 设置外框与闪帧接续（2026-10-16）

## 当前结论

重复入口和无修改保存的修复继续有效；整窗闪烁尚未修完。本批保留原生Settings单层外框规则，撤回未通过的PNG位图组合试验。Debug与Release已更新，不把单份试验零变化或功能测试通过写成全量无闪。Windows10排除本次验收，跨屏DPI未验证。

## 保留修改与功能回归

共享CSS的设置表单只在已有data-native-window呈现确认后，将网页边框颜色设为透明、圆角为0、阴影为none，保留边框宽度。DWM实现和业务WebView数量不变。新增verify-settings-native-frame.cjs：两主题、12/40字号、1/1.75/2比例共12组合，逐一比较整窗与所有后代几何，原生标记撤销后恢复原样，均通过。6项重复入口、字体重载和背景观察器测试通过。

settings-native-frame-real-repo使用Debug随包资源、用户配置副本、真实仓库9标签、隔离profile，无场景或DPI覆盖。延迟呈现回包并重复点击、延迟遮盖回包、四分类导航、嵌套返回均完成；五条关闭路径474/249/229/370/264ms，网页错误0。单组时序不代表所有负载下均小于500ms。此前209项Shell已通过，本批最终保留修改仅CSS及审计测试，没有再次跑无关全量。

## 原始桌面录像

以下目录均位于artifacts/pixel-followup-2026-10-16。Desktop Duplication、1820×1341整张Settings表面、固定首帧、RGB容差2、排除0像素；前景记录qualified=true，源时间戳无重复，解码DTS量化警告保留。没有过滤失败帧。

| 样本 | 采样帧/变化帧 | 结果 |
| --- | --- | --- |
| refresh-native-outline-probe | 316/0 | 临时浅色外框隔离单份通过；首次519ms，不能推导稳定无闪 |
| refresh-native-outline-probe-dark | 349/1 | 第65帧12796个边缘/角部像素变化，内部0；首次504ms |
| settings-native-frame-final-light | 323/4 | 正式浅色实现第58–60帧露出底层主界面，最坏2295787像素，其中内部2208412；第70帧4064个边缘/角部变化；首次542ms，复用317–378ms |
| settings-native-frame-final-dark | 351/2 | 正式深色第75–76帧12796个边缘/角部变化，内部0；首次524ms，复用294–359ms |

正式浅/深最大采样间隙39/34ms；sampledWholeSurfacePassed和fullPresentationVerified均false。浅色原始最坏帧已经目视确认主界面露底。DDA相对时间未精确配准UTC，不据此宣称已确定某个桥接阶段为唯一根因。网页重复外框会参与重复合成，但单层外框不足以修复原生遮盖交接。

正式浅色录像SHA256：31485e7d71ad6b4d373714095a4fcf110c1c25e5fd475d7daf44cae12b160b20。
正式深色录像SHA256：fd2290d7f51f60de903f59c6ce639b8b055ac197e6e6dc76e427e3277ea573f6。

浅色修改前后外沿128px比较共874752像素；匹配背景的阴影差异均未超容差，但底边2447个背景像素不匹配、外环尚未完全回到背景。因此shadow-regression.json仍明确allExternalPixelsPassed=false、fullShadowVerified=false，不登记完整系统阴影验收。

## 已撤回的组合候选

在用户已授权范围内，将先前原始PNG位图遮盖与本批单层网页外框组合。损坏PNG和越界2项定向通过；真实浅色327帧中58帧变化，最坏12796个边缘/角部像素，内部0，六次300–369ms。虽有速度收益，整表面仍失败，不符合保留条件。候选与恢复基线保存在refresh-bitmap-native-frame，临时编译文件已删除；ShellModalHost恢复SHA256为124BFB330AE90639AA26115380F57FF2DAA410099C11FE2AA0D1B48D6A940348。架构未变。

组合录像SHA256：c275ca91d495b5a37561476f3bc00110748fced32098b2aa2b95f9ba698bc82e。首次安装脚本因转义语法错误未执行；随后修正并安装，两项测试确实通过，不将前一次零匹配测试计入通过。

## 产物与清理

最终Debug/Release恢复构建分别3.09/3.03秒，均0警告0错误；两套各24个web文件逐字节一致。CSS MD5由52B45697D34F973360E6B85796E91493变为285BA9ED6A1C6BCDF78C53A8362110D9，其余三网页文件保持上一批哈希。临时native-outline-probe注入选项已移除；正式录像没有此注入。

接续的跳转行采集因定位检查点超时退出，未增加该项通过记录。驱动finally清理本批Augit1820/1800/10532/27044/2720/26308及对应profile；录屏器7008/5124/7852/10580和采样器13180/28300/26560/10104已退出。端口62838/54736/51078/61455/50152/50998无监听。历史拒绝清理路径未触碰；混合工作区未暂存、提交或回滚。

日常启动：powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1。正常关闭窗口停止；异常仅按此次启动输出PID执行taskkill /PID <本次PID> /T /F。

定向功能复验：node tools/audit/check-settings-close.cjs <Playwright模块路径> <新目录> --many-tabs --delay-cover --delay-present --nested --bundled-web --navigation。

录像复验：node tools/audit/capture-shadow-shell.cjs <Playwright模块路径> <新目录> Light --normal-entry --refresh-video --trace-surfaces --desktop-duplication --repeat-static-frames --activate-main。激活实际窗口后释放main-active-ready检查点；驱动finally清理。逐帧分析：python -B tools/audit/verify-refresh-surface.py <新目录> --require-stable。

下一步仍须解决遮盖交接中的内部露底和原生边缘闪动；55页及全模态、终端/冲突逐行、完整系统阴影四项目标保持未完成。

最终共享9资产、相关JS语法、文档声明和git diff --check通过；文档检查明确保留历史全量快照过期提示。七个本批隔离目录均不存在。PID21316已被系统复用于本批13:21:34启动的闲置MSBuild节点，核对命令行与创建时间后停止；没有按旧采样器身份直接结束复用PID。最终没有Augit或ffmpeg残留。
