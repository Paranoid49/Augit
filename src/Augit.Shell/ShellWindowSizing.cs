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

    /// <summary>把逻辑尺寸换算成物理像素。</summary>
    internal static int ScaleToPhysical(int logical, int dpi)
        => (int)Math.Round(logical * dpi / (double)BaselineDpi);

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
}

/// <summary>窗口的物理像素尺寸。</summary>
internal readonly record struct PhysicalSize(int Width, int Height);
