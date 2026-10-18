# 终端字号量化与八行栅格反证（2026-10-16）

## 结论

本批推进终端逐行配对，未修改产品字体、字号、行格或样式。实机证据排除本样本中“13×1.75为22.75物理px，而参考使用23px，显式取整可消除残差”的假设。八行已有行格一致，但文字栅格尚未完全相同；55页全模态、背景刷新、完整终端/冲突及系统阴影四项目标仍未完成。Windows10排除本次验收，跨屏DPI未验证。

## 来源与试验

参考使用此前实际PyCharm2026.2.1深色原图terminal-dark-pair-hook/reference-dark.png。实际本轮从普通场景入口打开真实Shell，固定启动表达式输出八行Ag 0123456789，未注入buffer；DPI168、JetBrains Mono13、行格18逻辑px、格宽8逻辑px。通过computer-use枚举并激活本轮Augit，点击编辑区使终端失焦，指针留在编辑区，终端采样带未被指针覆盖。无整窗指针清除门禁，不据此声称整窗无遮挡配对。

新增probe-terminal-raster-size.cjs，仅在--raster-size-probe隔离入口运行，顺序为原13px、向下取整22/1.75px、四舍五入23/1.75px、恢复13px。每次断言网格宽高、DPR和真实Shell全部内容保持，保存计算样式及系统原图；finally恢复字号。未更新产品设置或安装字体。

实际计算样式依次13px、12.5714px、13.1429px、13px，证明诊断条件生效。原13px、23/1.75px及恢复13px三张完整PNG逐字节相同，SHA256均b1d2f08b08ad80b35d35494cbd4642069f04209d0f3982ddad56773af4dd01ee。向下取整图SHA256为487fc2a5845c653b3bd299854522b02dba7e9f8ffb10d7938e7ca87b98f1fd4a，字形反而变差，不采纳。

## 八行量测

证据根artifacts/pixel-followup-2026-10-16/terminal-ink-diagnosis，live保存原始系统PNG、身份侧车和raster-size-probe.json，eight-row-raster-comparison.json保存每行具体坐标、阈值与原图哈希。各行按阈值160的亮墨迹左上锚点截取固定190×31原像素局部框，仅平移，不缩放。局部诊断不代替绝对位置、整窗配准及字体回退门禁。

| 条件 | 每行墨迹框 | 每行不同像素 / 超过RGB2 | 平均最大通道差 / 最大差 |
| --- | --- | --- | --- |
| 参考 | 178×21 | 不适用 | 不适用 |
| 当前13及取整23/1.75 | 178×21 | 1318 / 1237 | 5.0557 / 72 |
| 向下取整22/1.75 | 177×20 | 1547 / 1524 | 16.3973 / 162 |

八行每行残差计数一致，各条件行步长均32/31交替，与参考相同；没有逐行累计偏离。当前墨迹顶1167/1199/1230/1262/1293/1325/1356/1388；参考1236/1268/1299/1331/1362/1394/1425/1457。整窗及面板原点不同，不能用69px差值补偿产品。qualifiedPair及pixelRestorationVerified均false。

另只读比较独立来源安装TTF与参考JBR的Regular TTF，均273900字节，SHA256为a0bf60ef0f83c5ed4d7a75d45838548b1f6873372dfac88f71804491898d138f，全部17个sfnt表一致，包括glyf/hmtx/fpgm/prep/cvt。未复制参考字体用于分发。随包WOFF2仍为锁定a9cb1cd82332b23a47e3a1239d25d13c86d16c4220695e34b243effa999f45f2；本批没有解压WOFF2比表，因此不把TTF比对冒充WOFF2轮廓证明。初次fontTools导入失败，改为标准库只读解析TTF目录，未安装依赖。

## 验证及清理

2项新增探针测试135.22ms通过，覆盖四条件网格/内容不变、恢复13号以及截图失败恢复且不伪造完成报告。真实一次四条件取证完成并恢复；只运行相关测试和脚本语法检查，不运行全量或重新构建未改变的产品。

本轮Augit26976已由finally停止，65463无监听，Augit-terminal-pair-BDoQsj不存在。两项单测临时目录由finally删除；截图/JSON和诊断工具是保留证据。未启动PyCharm或常驻服务器，未重试历史拒绝的清理目录。用户混合改动保持，未暂存或提交。

产品四文件MD5未变：mockup.js=44D6C53684B74DBC6116FF25F7C22932、mockup.css=285BA9ED6A1C6BCDF78C53A8362110D9、live-data.js=CB897499424737392D7EF315E41F91E4、bridge.js=F297BF8769E249CA0D804F4F4EEEF5A9。

```powershell
# 单次隔离取证；观察并激活实际窗口、将焦点移出终端后，创建打印的terminal-ready文件。
node tools/audit/capture-terminal-pair.cjs <Playwright模块路径> <新目录> --theme=Dark --startup-rows --wait-foreground --raster-size-probe
node --test tools/audit/probe-terminal-raster-size.test.cjs
# 驱动finally退出并清理；异常时仅对本次输出PID操作。
taskkill /PID <本次PID> /T /F
# 日常启动；停止请正常关闭窗口。
powershell -NoProfile -ExecutionPolicy Bypass -File D:/github/Augit/tools/start-augit.ps1
```

后续应取得同一真实字体绘制上下文的提示、对比度及亚像素栅格证据，不能继续凭字号取整或单行偏移修补。当前产品规范不要求所有平台渲染像素绝对相等，但尚需完成实际同态、逐角色和逐状态证据，不能由本样本宣称终端已验收。
