using Augit.Shell;

namespace Augit.Shell.Tests;

/// <summary>
/// 外壳窗口尺寸换算的测试。
///
/// 背景：WebView2 按显示器缩放栅格化，窗口物理尺寸不换算就会拿到被缩小 1/scale 的
/// CSS 视口；视觉稿在 .augit-window 上要求 min-width:1024px / min-height:640px，
/// 不足时右侧与底部（状态栏、底部工具窗）被裁且 body 是 overflow:hidden，无法滚到。
/// 本机 175% 缩放下实测过：不换算只有 661x398 视口、状态栏落在 618~640 完全不可见。
/// </summary>
[TestClass]
public sealed class ShellWindowSizingTests
{
    /// <summary>实测 DPI：100% / 125% / 150% / 175% / 200% / 250%。</summary>
    private static readonly int[] MonitoredDpis = [96, 120, 144, 168, 192, 240];

    [TestMethod]
    public void 正常启动时使用系统DPI()
    {
        // 这是本次缺陷的回归点：默认路径曾经忽略系统 DPI，只在显式 --dpi 时才换算。
        Assert.AreEqual(168, ShellWindowSizing.ResolveEffectiveDpi(null, false, 168));
        Assert.AreEqual(240, ShellWindowSizing.ResolveEffectiveDpi(null, false, 240));
    }

    [TestMethod]
    public void 显式DPI覆盖优先于系统DPI()
    {
        Assert.AreEqual(120, ShellWindowSizing.ResolveEffectiveDpi(120, false, 168));
        Assert.AreEqual(96, ShellWindowSizing.ResolveEffectiveDpi(96, false, 168));
    }

    [TestMethod]
    public void 像素对照模式固定为基准DPI()
    {
        // --pixel-exact 要求一个 CSS 像素对应一个物理像素，此时不能按系统 DPI 放大窗口。
        Assert.AreEqual(96, ShellWindowSizing.ResolveEffectiveDpi(null, true, 168));
        // 显式 --dpi 仍然优先：审计可以同时指定两者。
        Assert.AreEqual(144, ShellWindowSizing.ResolveEffectiveDpi(144, true, 168));
    }

    [TestMethod]
    public void 取不到系统DPI时退化为基准值()
    {
        // GetDpiForSystem 失败返回 0；必须退化，否则会算出 0 像素窗口。
        Assert.AreEqual(96, ShellWindowSizing.ResolveEffectiveDpi(null, false, 0));
        Assert.AreEqual(96, ShellWindowSizing.ResolveEffectiveDpi(null, false, -1));
    }

    [TestMethod]
    public void 默认窗口在高DPI下放大到保持逻辑视口()
    {
        // 1180x760 逻辑；175% 缩放下必须是 2065x1330 物理像素，
        // 否则 CSS 视口只有 1180/1.75 宽，布局放不下。
        Assert.AreEqual(2065, ShellWindowSizing.ScaleToPhysical(ShellWindowSizing.DefaultLogicalWidth, 168));
        Assert.AreEqual(1330, ShellWindowSizing.ScaleToPhysical(ShellWindowSizing.DefaultLogicalHeight, 168));
    }

    [TestMethod]
    public void 最小窗口在高DPI下仍不小于视觉稿下限()
    {
        // 视觉稿 .augit-window 的 min-width/min-height 就是这两个值，
        // 换算后的物理尺寸除以缩放必须仍然 >= 1024x640 个 CSS 像素。
        Assert.AreEqual(1792, ShellWindowSizing.ScaleToPhysical(ShellWindowSizing.MinimumLogicalWidth, 168));
        Assert.AreEqual(1120, ShellWindowSizing.ScaleToPhysical(ShellWindowSizing.MinimumLogicalHeight, 168));
    }

    [TestMethod]
    public void 换算后视口不小于视觉稿下限()
    {
        // 这条断言直接表达"换算必须让 CSS 视口装得下布局"这个业务要求：
        // 物理尺寸按同一比例折回逻辑像素后，不得小于视觉稿声明的最小尺寸。
        foreach (int dpi in MonitoredDpis)
        {
            int width = ShellWindowSizing.ScaleToPhysical(ShellWindowSizing.MinimumLogicalWidth, dpi);
            int height = ShellWindowSizing.ScaleToPhysical(ShellWindowSizing.MinimumLogicalHeight, dpi);
            double scale = dpi / (double)ShellWindowSizing.BaselineDpi;
            Assert.IsGreaterThanOrEqualTo(
                ShellWindowSizing.MinimumLogicalWidth - 1,
                width / scale,
                $"{dpi} DPI 下换算后的逻辑宽度只有 {width / scale}，小于视觉稿下限 1024。");
            Assert.IsGreaterThanOrEqualTo(
                ShellWindowSizing.MinimumLogicalHeight - 1,
                height / scale,
                $"{dpi} DPI 下换算后的逻辑高度只有 {height / scale}，小于视觉稿下限 640。");
        }
    }

    [TestMethod]
    public void 基准DPI下换算结果不变()
    {
        foreach (int value in new[] { 640, 760, 1024, 1180, 2065 })
        {
            Assert.AreEqual(value, ShellWindowSizing.ScaleToPhysical(value, ShellWindowSizing.BaselineDpi));
        }
    }

    [TestMethod]
    public void 超出工作区的窗口收敛到工作区()
    {
        // 高缩放的笔记本屏幕上 1180 逻辑宽会超过屏幕宽度，必须收敛，否则窗口比屏幕还大。
        Assert.AreEqual(1920, ShellWindowSizing.FitToWorkArea(2065, 1920));
        Assert.AreEqual(1080, ShellWindowSizing.FitToWorkArea(1330, 1080));
    }

    [TestMethod]
    public void 装得下的窗口保持原尺寸()
    {
        // 与上一条配对：证明收敛只在真的超出时发生，而不是无条件截断。
        Assert.AreEqual(2065, ShellWindowSizing.FitToWorkArea(2065, 3840));
        Assert.AreEqual(2065, ShellWindowSizing.FitToWorkArea(2065, 2065));
    }

    [TestMethod]
    public void 取不到工作区时不收敛()
    {
        // SystemParametersInfo 失败时工作区为 0，此时不能把窗口截成 0 像素。
        Assert.AreEqual(2065, ShellWindowSizing.FitToWorkArea(2065, 0));
        Assert.AreEqual(2065, ShellWindowSizing.FitToWorkArea(2065, -1));
    }

    [TestMethod]
    public void 正常启动按显示器缩放换算窗口以保证CSS视口()
    {
        // 走生产调用链（ResolveEffectiveDpi -> InitialWindow / MinimumWindow）。
        // 关键点：CSS 视口 = 物理尺寸 / 显示器缩放，而显示器缩放来自系统而不是解析结果，
        // 否则把生效 DPI 退化成基准值时会因为两边同比例变化而"自洽地"通过。
        foreach (int systemDpi in MonitoredDpis)
        {
            double rasterizationScale = systemDpi / (double)ShellWindowSizing.BaselineDpi;
            int dpi = ShellWindowSizing.ResolveEffectiveDpi(null, false, systemDpi);
            PhysicalSize initial = ShellWindowSizing.InitialWindow(
                ShellWindowSizing.DefaultLogicalWidth,
                ShellWindowSizing.DefaultLogicalHeight,
                dpi,
                workWidth: 20000,
                workHeight: 20000);
            PhysicalSize minimum = ShellWindowSizing.MinimumWindow(dpi, workWidth: 20000, workHeight: 20000);

            Assert.IsGreaterThanOrEqualTo(
                (double)ShellWindowSizing.MinimumLogicalWidth,
                initial.Width / rasterizationScale,
                $"{systemDpi} DPI 下默认窗口换算出的 CSS 宽度不足以容纳布局。");
            Assert.IsGreaterThanOrEqualTo(
                (double)ShellWindowSizing.MinimumLogicalHeight,
                initial.Height / rasterizationScale,
                $"{systemDpi} DPI 下默认窗口换算出的 CSS 高度不足以容纳布局。");
            Assert.IsGreaterThanOrEqualTo(
                (double)ShellWindowSizing.MinimumLogicalWidth,
                minimum.Width / rasterizationScale,
                $"{systemDpi} DPI 下最小窗口换算出的 CSS 宽度小于视觉稿下限。");
            Assert.IsGreaterThanOrEqualTo(
                (double)ShellWindowSizing.MinimumLogicalHeight,
                minimum.Height / rasterizationScale,
                $"{systemDpi} DPI 下最小窗口换算出的 CSS 高度小于视觉稿下限。");
        }
    }

    [TestMethod]
    public void 本机一百七十五缩放下的窗口物理尺寸()
    {
        // 本机实测基线：显示器 175% 缩放。窗口必须是 2065x1330 物理像素，
        // 换成 1180x760 时 CSS 视口只有 674x434，状态栏被排到视口之外（实测 618~640）。
        int dpi = ShellWindowSizing.ResolveEffectiveDpi(null, false, 168);
        PhysicalSize size = ShellWindowSizing.InitialWindow(
            ShellWindowSizing.DefaultLogicalWidth,
            ShellWindowSizing.DefaultLogicalHeight,
            dpi,
            workWidth: 20000,
            workHeight: 20000);
        Assert.AreEqual(2065, size.Width);
        Assert.AreEqual(1330, size.Height);
    }

    [TestMethod]
    public void 像素对照模式不放大窗口()
    {
        // --pixel-exact 的契约是一个 CSS 像素对应一个物理像素；
        // 若这里按系统 DPI 放大，视觉稿的逐像素对照就会全部错位。
        int dpi = ShellWindowSizing.ResolveEffectiveDpi(null, true, 168);
        PhysicalSize size = ShellWindowSizing.InitialWindow(
            ShellWindowSizing.DefaultLogicalWidth,
            ShellWindowSizing.DefaultLogicalHeight,
            dpi,
            workWidth: 20000,
            workHeight: 20000);
        Assert.AreEqual(ShellWindowSizing.DefaultLogicalWidth, size.Width);
        Assert.AreEqual(ShellWindowSizing.DefaultLogicalHeight, size.Height);
    }

    [TestMethod]
    public void 高缩放小屏幕上窗口收敛到工作区()
    {
        // 1180 逻辑宽在 175% 下需要 2065 物理像素；工作区只有 1920 时必须收敛。
        int dpi = ShellWindowSizing.ResolveEffectiveDpi(null, false, 168);
        PhysicalSize size = ShellWindowSizing.InitialWindow(
            ShellWindowSizing.DefaultLogicalWidth,
            ShellWindowSizing.DefaultLogicalHeight,
            dpi,
            workWidth: 1920,
            workHeight: 1080);
        Assert.AreEqual(1920, size.Width);
        Assert.AreEqual(1080, size.Height);
    }

    // ---- 窗口摆放恢复（§6.6「已恢复窗口尺寸不得被默认值覆盖」）----

    /// <summary>一块 3840x2160 的虚拟屏幕，足够放下默认窗口。</summary>
    private static readonly VirtualScreen Screen = new(0, 0, 3840, 2160);

    [TestMethod]
    public void 恢复的窗口尺寸优先于默认值()
    {
        PhysicalPlacement placement = ShellWindowSizing.ResolveStartupPlacement(
            new SavedPlacement(200, 150, 1400, 900, false),
            overrideWidth: null,
            overrideHeight: null,
            effectiveDpi: 96,
            workWidth: 3840,
            workHeight: 2160,
            screen: Screen);
        Assert.AreEqual(1400, placement.Width);
        Assert.AreEqual(900, placement.Height);
        Assert.AreEqual(200, placement.X);
        Assert.AreEqual(150, placement.Y);
    }

    [TestMethod]
    public void 显式命令行尺寸覆盖恢复值()
    {
        // 审计矩阵用 --width/--height 固定尺寸；此时设置里的用户尺寸不得生效，
        // 否则同一场景在不同机器上尺寸不同，截图无法比较。
        PhysicalPlacement placement = ShellWindowSizing.ResolveStartupPlacement(
            new SavedPlacement(0, 0, 1400, 900, false),
            overrideWidth: 1024,
            overrideHeight: 640,
            effectiveDpi: 96,
            workWidth: 3840,
            workHeight: 2160,
            screen: Screen);
        Assert.AreEqual(1024, placement.Width);
        Assert.AreEqual(640, placement.Height);
    }

    [TestMethod]
    public void 恢复的尺寸低于布局下限时收敛到下限()
    {
        // 设置里可能是旧版本或损坏的值；低于 .augit-window 的 1024x640 会裁掉界面。
        PhysicalPlacement placement = ShellWindowSizing.ResolveStartupPlacement(
            new SavedPlacement(null, null, 400, 300, false),
            overrideWidth: null,
            overrideHeight: null,
            effectiveDpi: 96,
            workWidth: 3840,
            workHeight: 2160,
            screen: Screen);
        Assert.AreEqual(ShellWindowSizing.MinimumLogicalWidth, placement.Width);
        Assert.AreEqual(ShellWindowSizing.MinimumLogicalHeight, placement.Height);
    }

    [TestMethod]
    public void 非法恢复值回退到默认尺寸()
    {
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            PhysicalPlacement placement = ShellWindowSizing.ResolveStartupPlacement(
                new SavedPlacement(null, null, bad, bad, false),
                overrideWidth: null,
                overrideHeight: null,
                effectiveDpi: 96,
                workWidth: 3840,
                workHeight: 2160,
                screen: Screen);
            Assert.AreEqual(ShellWindowSizing.DefaultLogicalWidth, placement.Width);
            Assert.AreEqual(ShellWindowSizing.DefaultLogicalHeight, placement.Height);
        }
    }

    [TestMethod]
    public void 恢复的位置仍在屏幕内时被采用()
    {
        (int x, int y) = ShellWindowSizing.ResolveStartupPosition(600, 400, 96, Screen);
        Assert.AreEqual(600, x);
        Assert.AreEqual(400, y);
    }

    [TestMethod]
    public void 恢复的位置越界时回到默认位置()
    {
        // 显示器被拔掉后设置里会留下屏幕外的坐标；直接用会让窗口出现在用户看不到的地方。
        (int x, int y) = ShellWindowSizing.ResolveStartupPosition(9000, 5000, 96, Screen);
        Assert.AreEqual(ShellWindowSizing.DefaultWindowX, x);
        Assert.AreEqual(ShellWindowSizing.DefaultWindowY, y);
        (int left, int _) = ShellWindowSizing.ResolveStartupPosition(-4000, 100, 96, Screen);
        Assert.AreEqual(ShellWindowSizing.DefaultWindowX, left);
    }

    [TestMethod]
    public void 取不到虚拟屏幕时不否决恢复位置()
    {
        // 取不到屏幕尺寸时不能把所有恢复位置都判为无效，否则每次启动都回到默认位置。
        (int x, int y) = ShellWindowSizing.ResolveStartupPosition(600, 400, 96, new VirtualScreen(0, 0, 0, 0));
        Assert.AreEqual(600, x);
        Assert.AreEqual(400, y);
    }

    [TestMethod]
    public void 标题栏探针点判定边界()
    {
        // 探针点必须落在虚拟屏幕内；右边界是开区间，避免整窗刚好贴在屏幕外仍被判定可见。
        VirtualScreen screen = new(0, 0, 1000, 800);
        Assert.IsTrue(ShellWindowSizing.IsTitleBarReachable(0, 0, screen));
        Assert.IsTrue(ShellWindowSizing.IsTitleBarReachable(-79, 0, screen));
        Assert.IsFalse(ShellWindowSizing.IsTitleBarReachable(-81, 0, screen));
        Assert.IsFalse(ShellWindowSizing.IsTitleBarReachable(1000 - ShellWindowSizing.TitleBarProbeX, 0, screen));
        Assert.IsFalse(ShellWindowSizing.IsTitleBarReachable(0, 800 - ShellWindowSizing.TitleBarProbeY, screen));
    }

    [TestMethod]
    public void 恢复最大化状态()
    {
        PhysicalPlacement placement = ShellWindowSizing.ResolveStartupPlacement(
            new SavedPlacement(100, 100, 1180, 760, true),
            overrideWidth: null,
            overrideHeight: null,
            effectiveDpi: 96,
            workWidth: 3840,
            workHeight: 2160,
            screen: Screen);
        Assert.IsTrue(placement.IsMaximized);
    }

    [TestMethod]
    public void 显式尺寸时不恢复最大化状态()
    {
        // 审计要的是确定的窗口尺寸；恢复最大化会让截图尺寸不可预期。
        PhysicalPlacement placement = ShellWindowSizing.ResolveStartupPlacement(
            new SavedPlacement(100, 100, 1180, 760, true),
            overrideWidth: 1180,
            overrideHeight: 760,
            effectiveDpi: 96,
            workWidth: 3840,
            workHeight: 2160,
            screen: Screen);
        Assert.IsFalse(placement.IsMaximized);
    }

    [TestMethod]
    public void 逻辑单位与物理像素可往返换算()
    {
        // 设置文件存逻辑单位，因此"保存再读回"必须还原同一个逻辑尺寸，
        // 否则用户每次退出再启动窗口都会缩一点或长一点。
        foreach (int dpi in MonitoredDpis)
        {
            foreach (int logical in new[] { 640, 760, 1024, 1180, 1400 })
            {
                double roundTrip = ShellWindowSizing.ToLogical(
                    ShellWindowSizing.ScaleToPhysical(logical, dpi), dpi);
                Assert.AreEqual((double)logical, roundTrip, 1.0, $"{dpi} DPI 下 {logical} 往返不一致。");
            }
        }
    }

    [TestMethod]
    public void 恢复位置不因取整而偏移()
    {
        // 端到端实测过：把 (300,200) 物理位置按 175% 存成逻辑 (171.42857, 114.28571) 后，
        // 先取整再换算会得到 299；必须直接按小数换算，否则用户的窗口会固定偏 1 像素。
        (int x, int y) = ShellWindowSizing.ResolveStartupPosition(
            171.42857142857142, 114.28571428571429, 168, Screen);
        Assert.AreEqual(300, x);
        Assert.AreEqual(200, y);
    }

    [TestMethod]
    public void 位置保存恢复往返不漂移()
    {
        foreach (int dpi in MonitoredDpis)
        {
            // 只用"标题栏探针点仍落在 Screen 内"的位置：越界位置回退到默认是另一条测试覆盖的正确行为。
            foreach ((int x, int y) in new[] { (0, 0), (80, 80), (300, 200), (1920, 1080), (-79, 40) })
            {
                double logicalX = ShellWindowSizing.ToLogical(x, dpi);
                double logicalY = ShellWindowSizing.ToLogical(y, dpi);
                (int rx, int ry) = ShellWindowSizing.ResolveStartupPosition(logicalX, logicalY, dpi, Screen);
                Assert.AreEqual(x, rx, $"{dpi} DPI 下 x={x} 往返后变成 {rx}。");
                Assert.AreEqual(y, ry, $"{dpi} DPI 下 y={y} 往返后变成 {ry}。");
            }
        }
    }
}
