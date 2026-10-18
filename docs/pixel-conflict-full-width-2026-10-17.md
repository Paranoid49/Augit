# 三栏完整编辑区逐行诊断（2026-10-17）

此前compare-conflict-lines.py只比较每栏约190像素宽的短文字片段，不能代表整行。新增--full-width模式，在既有1730×1215原始系统图中固定覆盖y=196至1127的全部编辑区。按三栏及两个连接带分五列，每列按38.5物理像素行格划25段，包含末端空白；不移动、重采样、按字形裁切或修改PNG。

三状态共375区域，每状态1610630像素完整计入，覆盖测试证明每像素恰好一次。新增行尾差异反例：旧短裁框看不到x=400处变化，全宽模式必须保留。6项定向测试通过，0.203秒。

证据为artifacts/pixel-followup-2026-10-17/conflict-full-width-diagnostic.json。原图仍来自10月15日reference-clean和10月16日conflict-gutter-inside-final，所有原图哈希重验；不是本轮正常入口新采集，不能改变历史证据等级。

| 状态 | 全编辑区不同像素（精确RGB） | 完全相同区域/125 |
| --- | ---: | ---: |
| 初始 | 25942 | 48 |
| 接受左侧 | 25665 | 46 |
| 全部处理 | 26695 | 44 |

相同区域多数为空白，不能将138/375当文字通过率。光标、编号槽和连接带均纳入差异，因此全宽墨迹外框不再是纯文字外框，不能据此固定平移字形。既有两端短夹具只有三行，长文本、滚动、IME、外部更新与正常入口同态采集仍缺；pixelRestorationVerified=false，整体未完成。

复验：`D:/Python/Python3119/python.exe -B tools/audit/compare-conflict-lines.py artifacts/pixel-followup-2026-10-15/conflict-reference-clean artifacts/pixel-followup-2026-10-16/conflict-gutter-inside-final artifacts/conflict-full-width-new.json --full-width`。输出须不存在。仅离线分析，无应用、服务器或网络进程；测试临时目录自动回收。
