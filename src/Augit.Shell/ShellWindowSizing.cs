namespace Augit.Shell;

/// <summary>
/// 外壳窗口的逻辑尺寸到物理像素的换算。
///
/// WebView2 按显示器缩放栅格化（175% 缩放下 1 CSS 像素 = 1.75 物理像素），
/// 因此窗口的物理尺寸必须按同一比例换算，否则界面拿到的是被缩小 1/scale 的逻辑视口。
/// 视觉稿在 <c>.augit-window</c> 上声明 <c>min-width:1024px; min-height:640px</c>，
/// 逻辑视口不足时右侧与底部（状态栏、底部工具窗）会被裁掉，且 <c>body</c> 是
/// <c>overflow:hidden</c>，用户既看不到也滚动不到。
///
/// 实测（本机 175% 缩放）：不换算时窗口 1180x760 物理像素只给出 661x398 的 CSS 视口，
/// 状态栏被排在纵向 618~640，完全落在视口之外；按 DPI 换算后视口为 1167x724，
/// 视口高度与内容高度相等，状态栏回到 702~724 正常可见。
/// </summary>
internal static class ShellWindowSizing
{
    /// <summary>默认窗口的逻辑尺寸（与视觉稿的对照宽度一致）。</summary>
    internal const int DefaultLogicalWidth = 1180;

    internal const int DefaultLogicalHeight = 760;

    /// <summary>最小窗口的逻辑尺寸；低于此值布局无法容纳三个区域（规格 §4.2）。</summary>
    internal const int MinimumLogicalWidth = 1024;

    internal const int MinimumLogicalHeight = 640;

    /// <summary>DPI 基准；Windows 上 100% 缩放对应 96。</summary>
    internal const int BaselineDpi = 96;

    /// <summary>首次启动（没有可恢复位置）时的窗口左上角，物理像素。</summary>
    internal const int DefaultWindowX = 80;

    internal const int DefaultWindowY = 80;

    /// <summary>标题栏探针点在窗口内的偏移，物理像素；用来判断窗口是否还可被拖到。</summary>
    internal const int TitleBarProbeX = 80;

    internal const int TitleBarProbeY = 20;

    /// <summary>
    /// 计算实际生效的 DPI。优先级：
    /// 显式 <c>--dpi</c>（审计用，同时把 WebView2 栅格化比例固定为 dpi/96）；
    /// <c>--pixel-exact</c>（要求一个 CSS 像素对应一个物理像素，因此固定为基准值）；
    /// 其余情况使用系统 DPI——这是正常启动路径，必须换算，否则高缩放下界面被裁。
    /// </summary>
    internal static int ResolveEffectiveDpi(int? overrideDpi, bool pixelExact, int systemDpi)
    {
        if (overrideDpi is { } dpi)
        {
            return dpi;
        }

        if (pixelExact)
        {
            return BaselineDpi;
        }

        // GetDpiForSystem 失败时返回 0；退化为基准值而不是除零或缩成 0 像素窗口。
        return systemDpi > 0 ? systemDpi : BaselineDpi;
    }

    /// <summary>
    /// 把逻辑尺寸换算成物理像素。接受小数：设置里保存的是逻辑单位，
    /// 先取整再换算会引入最多 1 像素偏移，反复保存/恢复后会固定偏在错误位置上。
    /// </summary>
    internal static int ScaleToPhysical(double logical, int dpi)
        => (int)Math.Round(logical * dpi / BaselineDpi);

    /// <summary>
    /// 收敛到工作区尺寸。高缩放的笔记本屏幕上，按逻辑尺寸放大后的窗口可能比屏幕还大；
    /// 工作区为 0（取不到）时不收敛。
    /// </summary>
    internal static int FitToWorkArea(int physical, int workArea)
        => workArea > 0 && physical > workArea ? workArea : physical;

    /// <summary>
    /// 初始窗口的物理尺寸：按生效 DPI 换算后收敛到工作区。
    /// 这是创建窗口的实际调用链，测试必须覆盖它而不是只覆盖单步算术——
    /// 只测算术时，把生效 DPI 退化成基准值（缺陷原状）仍然会全部通过。
    /// </summary>
    internal static PhysicalSize InitialWindow(
        int logicalWidth,
        int logicalHeight,
        int effectiveDpi,
        int workWidth,
        int workHeight)
        => new(
            FitToWorkArea(ScaleToPhysical(logicalWidth, effectiveDpi), workWidth),
            FitToWorkArea(ScaleToPhysical(logicalHeight, effectiveDpi), workHeight));

    /// <summary>最小窗口的物理尺寸；同样收敛到工作区，避免屏幕本身小于布局下限时窗口无法缩小。</summary>
    internal static PhysicalSize MinimumWindow(int effectiveDpi, int workWidth, int workHeight)
        => InitialWindow(MinimumLogicalWidth, MinimumLogicalHeight, effectiveDpi, workWidth, workHeight);

    /// <summary>
    /// 解析一个逻辑尺寸。设置里恢复的值优先于默认值，但不得低于布局下限——
    /// 否则界面会被裁（§6.6「已恢复窗口尺寸不得被默认值覆盖」同时也不允许恢复到装不下的尺寸）。
    /// 非法值（缺失、NaN、无穷）按默认值处理。
    /// </summary>
    internal static int ResolveLogicalDimension(double? saved, int fallback, int minimum)
        => saved is { } value && double.IsFinite(value)
            ? Math.Max((int)Math.Round(value), minimum)
            : fallback;

    /// <summary>物理像素换算回逻辑单位，使设置文件不受显示器 DPI 影响。</summary>
    internal static double ToLogical(int physical, int dpi)
        => dpi > 0 ? physical * BaselineDpi / (double)dpi : physical;

    /// <summary>
    /// 标题栏上的探针点：恢复的位置至少要让它落在虚拟屏幕内，否则窗口会被放到用户看不到的地方
    /// （显示器被拔掉、分辨率变小、投影切换都会造成这种陈旧位置）。
    /// </summary>
    internal static bool IsTitleBarReachable(int x, int y, VirtualScreen screen)
    {
        if (screen.Width <= 0 || screen.Height <= 0)
        {
            // 取不到虚拟屏幕时不做否决，避免把所有恢复位置都判为无效。
            return true;
        }

        int probeX = x + TitleBarProbeX;
        int probeY = y + TitleBarProbeY;
        return probeX >= screen.X && probeX < screen.X + screen.Width
            && probeY >= screen.Y && probeY < screen.Y + screen.Height;
    }

    /// <summary>
    /// 启动时的窗口位置：设置里恢复的位置优先，越界或非法时回到默认位置 (80, 80)。
    /// </summary>
    internal static (int X, int Y) ResolveStartupPosition(
        double? savedLeft,
        double? savedTop,
        int effectiveDpi,
        VirtualScreen screen)
    {
        if (savedLeft is { } left && savedTop is { } top && double.IsFinite(left) && double.IsFinite(top))
        {
            int x = ScaleToPhysical(left, effectiveDpi);
            int y = ScaleToPhysical(top, effectiveDpi);
            if (IsTitleBarReachable(x, y, screen))
            {
                return (x, y);
            }
        }

        return (DefaultWindowX, DefaultWindowY);
    }

    /// <summary>
    /// 启动时完整的窗口摆放：尺寸、位置与最大化状态。
    /// 优先级为「显式 --width/--height」＞「设置里恢复的值」＞「默认值」；
    /// 显式给尺寸时（审计矩阵）不恢复最大化状态，保证窗口尺寸是确定的。
    /// </summary>
    internal static PhysicalPlacement ResolveStartupPlacement(
        SavedPlacement saved,
        int? overrideWidth,
        int? overrideHeight,
        int effectiveDpi,
        int workWidth,
        int workHeight,
        VirtualScreen screen)
    {
        int logicalWidth = overrideWidth
            ?? ResolveLogicalDimension(saved.Width, DefaultLogicalWidth, MinimumLogicalWidth);
        int logicalHeight = overrideHeight
            ?? ResolveLogicalDimension(saved.Height, DefaultLogicalHeight, MinimumLogicalHeight);
        PhysicalSize size = InitialWindow(logicalWidth, logicalHeight, effectiveDpi, workWidth, workHeight);
        (int x, int y) = ResolveStartupPosition(saved.Left, saved.Top, effectiveDpi, screen);
        bool maximized = overrideWidth is null && overrideHeight is null && saved.IsMaximized;
        return new PhysicalPlacement(x, y, size.Width, size.Height, maximized);
    }
}

/// <summary>窗口的物理像素尺寸。</summary>
internal readonly record struct PhysicalSize(int Width, int Height);

/// <summary>窗口的物理像素摆放。</summary>
internal readonly record struct PhysicalPlacement(int X, int Y, int Width, int Height, bool IsMaximized);

/// <summary>设置文件里恢复的窗口摆放；尺寸与位置都是逻辑单位。</summary>
internal readonly record struct SavedPlacement(double? Left, double? Top, double Width, double Height, bool IsMaximized);

/// <summary>所有显示器的物理像素并集（SM_*VIRTUALSCREEN）。</summary>
internal readonly record struct VirtualScreen(int X, int Y, int Width, int Height);
