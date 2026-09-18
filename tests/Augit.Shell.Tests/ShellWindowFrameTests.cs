using Augit.Shell;

namespace Augit.Shell.Tests;

/// <summary>
/// 无 caption 窗口的边框与命中测试规则。
///
/// 背景：视觉稿自绘标题栏与窗口按钮，窗口不能带 Win32 caption，否则右上角会同时出现
/// 原生按钮和自绘按钮（同一处两套关闭按钮）。去掉 caption 后拖动/缩放/最大化全靠
/// WM_NCHITTEST，因此这里的判定是「窗口还能不能拖动、还能不能缩放、标题栏控件还能不能点」
/// 的唯一依据，必须有测试兜住。
/// </summary>
[TestClass]
public sealed class ShellWindowFrameTests
{
    /// <summary>100% 缩放下 8 逻辑像素的抓取宽度。</summary>
    private const int Border96 = 8;

    private static readonly WindowFrameRect Window = new(100, 200, 1500, 1100);

    [TestMethod]
    public void 四边与四角返回对应缩放码()
    {
        // 左/右/上/下
        Assert.AreEqual(ShellWindowFrame.HtLeft, Hit(Window.Left + 2, 600, 96));
        Assert.AreEqual(ShellWindowFrame.HtRight, Hit(Window.Right - 2, 600, 96));
        Assert.AreEqual(ShellWindowFrame.HtTop, Hit(700, Window.Top + 2, 96));
        Assert.AreEqual(ShellWindowFrame.HtBottom, Hit(700, Window.Bottom - 2, 96));
        // 四角优先于单边：左上角必须给对角缩放，而不是只横向缩放
        Assert.AreEqual(ShellWindowFrame.HtTopLeft, Hit(Window.Left + 2, Window.Top + 2, 96));
        Assert.AreEqual(ShellWindowFrame.HtTopRight, Hit(Window.Right - 2, Window.Top + 2, 96));
        Assert.AreEqual(ShellWindowFrame.HtBottomLeft, Hit(Window.Left + 2, Window.Bottom - 2, 96));
        Assert.AreEqual(ShellWindowFrame.HtBottomRight, Hit(Window.Right - 2, Window.Bottom - 2, 96));
    }

    [TestMethod]
    public void 抓取宽度边界正好落在第8像素()
    {
        // 第 7 像素仍是缩放区，第 8 像素开始回到客户区；这条边界决定鼠标指针形状，
        // 差一像素就会出现「贴着边框却拉不动」。
        Assert.AreEqual(ShellWindowFrame.HtLeft, Hit(Window.Left + Border96 - 1, 600, 96));
        Assert.AreEqual(ShellWindowFrame.HtClient, Hit(Window.Left + Border96, 600, 96));
    }

    [TestMethod]
    public void 标题栏返回客户区而不是caption()
    {
        // 关键回归点：标题栏左侧是主菜单、工作区、分支等真实控件。
        // 返回 HTCAPTION 会让系统在按下瞬间进入移动循环，这些控件永远收不到点击。
        Assert.AreEqual(ShellWindowFrame.HtClient, Hit(Window.Left + 300, Window.Top + 20, 96));
        // 标题栏右侧的自绘窗口按钮同样必须留在客户区，否则三个按钮全部点不到。
        Assert.AreEqual(ShellWindowFrame.HtClient, Hit(Window.Right - 60, Window.Top + 20, 96));
    }

    [TestMethod]
    public void 标题栏正下方正文仍是客户区()
    {
        Assert.AreEqual(ShellWindowFrame.HtClient, Hit(Window.Left + 40, Window.Top + 44 + 10, 96));
        Assert.AreEqual(ShellWindowFrame.HtClient, Hit(700, 600, 96));
    }

    [TestMethod]
    public void 最大化时不返回缩放码()
    {
        // 最大化时窗口贴屏幕边缘：返回缩放码会让屏幕边界出现缩放指针，一拖就把窗口变成缩放。
        Assert.AreEqual(ShellWindowFrame.HtClient, Hit(Window.Left, Window.Top, 96, maximized: true));
        Assert.AreEqual(ShellWindowFrame.HtClient, Hit(Window.Right - 1, Window.Bottom - 1, 96, maximized: true));
        Assert.AreEqual(ShellWindowFrame.HtClient, Hit(Window.Left, 600, 96, maximized: true));
    }

    [TestMethod]
    public void 抓取宽度随DPI放大()
    {
        // 175% 缩放：8 逻辑像素 = 14 物理像素；不换算则抓取区只有视觉宽度的一半，很难点中。
        Assert.AreEqual(14, ShellWindowFrame.ResolveResizeBorder(168));
        Assert.AreEqual(21, ShellWindowFrame.ResolveResizeBorder(252));
        Assert.AreEqual(ShellWindowFrame.HtLeft, Hit(Window.Left + 13, 600, 168));
        Assert.AreEqual(ShellWindowFrame.HtClient, Hit(Window.Left + 14, 600, 168));
    }

    [TestMethod]
    public void 高DPI下抓取宽度不塌缩()
    {
        // DPI 异常（取不到系统 DPI 时上层会退化为 96）也不能得到 0 宽的抓取区。
        Assert.AreEqual(ShellWindowFrame.MinimumResizeBorder, ShellWindowFrame.ResolveResizeBorder(1));
        Assert.AreEqual(ShellWindowFrame.MinimumResizeBorder, ShellWindowFrame.ResolveResizeBorder(0));
    }

    [TestMethod]
    public void 退化矩形不产生缩放码()
    {
        // 窗口还没建好时 GetWindowRect 可能得到空矩形；此时给缩放码会让窗口无法点击。
        Assert.AreEqual(ShellWindowFrame.HtClient, Hit(0, 0, 96, window: new WindowFrameRect(10, 10, 10, 10)));
        Assert.AreEqual(ShellWindowFrame.HtClient, Hit(0, 0, 96, window: new WindowFrameRect(10, 10, 5, 10)));
    }

    [TestMethod]
    public void 最大化客户区钉在工作区()
    {
        // 可缩放窗口最大化时窗口矩形会外扩一圈边框；客户区必须钉回工作区，
        // 否则界面被推到屏幕外，标题栏右侧按钮被裁掉。
        WindowFrameRect monitor = new(0, 0, 1920, 1080);
        WindowFrameRect work = new(0, 0, 1920, 1040);
        Assert.AreEqual(work, ShellWindowFrame.MaximizedClientRect(monitor, work));
    }

    [TestMethod]
    public void 取不到工作区时退回显示器矩形()
    {
        // GetMonitorInfo 失败得到空矩形时不能把客户区设成 0x0，否则整个界面消失。
        WindowFrameRect monitor = new(0, 0, 1920, 1080);
        Assert.AreEqual(monitor, ShellWindowFrame.MaximizedClientRect(monitor, default));
        Assert.AreEqual(monitor, ShellWindowFrame.MaximizedClientRect(monitor, new WindowFrameRect(5, 5, 5, 900)));
    }

    [TestMethod]
    public void 网页层报告的边映射到缩放码()
    {
        // WebView2 的子窗口铺满客户区，系统把边缘的命中测试交给子窗口，窗口自己的
        // WM_NCHITTEST 不会被问到；边缘缩放只能由网页层报告，因此这张映射表必须完整。
        Assert.AreEqual(ShellWindowFrame.HtLeft, ShellWindowFrame.ResizeCode("left"));
        Assert.AreEqual(ShellWindowFrame.HtRight, ShellWindowFrame.ResizeCode("right"));
        Assert.AreEqual(ShellWindowFrame.HtTop, ShellWindowFrame.ResizeCode("top"));
        Assert.AreEqual(ShellWindowFrame.HtBottom, ShellWindowFrame.ResizeCode("bottom"));
        Assert.AreEqual(ShellWindowFrame.HtTopLeft, ShellWindowFrame.ResizeCode("topleft"));
        Assert.AreEqual(ShellWindowFrame.HtTopRight, ShellWindowFrame.ResizeCode("topright"));
        Assert.AreEqual(ShellWindowFrame.HtBottomLeft, ShellWindowFrame.ResizeCode("bottomleft"));
        Assert.AreEqual(ShellWindowFrame.HtBottomRight, ShellWindowFrame.ResizeCode("bottomright"));
        Assert.AreEqual(ShellWindowFrame.HtTopLeft, ShellWindowFrame.ResizeCode("TopLeft"));
    }

    [TestMethod]
    public void 未知的边不产生缩放码()
    {
        // 拼错的边不能退化成某个方向：宁可什么都不做，也不能出现方向错误的缩放。
        Assert.AreEqual(0, ShellWindowFrame.ResizeCode(null));
        Assert.AreEqual(0, ShellWindowFrame.ResizeCode(""));
        Assert.AreEqual(0, ShellWindowFrame.ResizeCode("centre"));
        Assert.AreEqual(0, ShellWindowFrame.ResizeCode("left "));
    }

    [TestMethod]
    public void 双击判定要求同点且落在双击时间内()
    {
        // 窗口没有 caption，输入层不会产生 WM_NCLBUTTONDBLCLK，双击最大化只能自己判定。
        Assert.IsTrue(ShellWindowFrame.IsCaptionDoubleClick(1000, 500, 300, 1200, 501, 302, 500, 2, 2));
        Assert.IsTrue(ShellWindowFrame.IsCaptionDoubleClick(1000, 500, 300, 1500, 500, 300, 500, 2, 2));
    }

    [TestMethod]
    public void 双击判定排除超时与移动过远的情况()
    {
        // 超过双击时间：是两次独立单击。
        Assert.IsFalse(ShellWindowFrame.IsCaptionDoubleClick(1000, 500, 300, 1600, 500, 300, 500, 2, 2));
        // 第二次按下时已经挪出双击矩形：用户是在拖动，不是双击。
        Assert.IsFalse(ShellWindowFrame.IsCaptionDoubleClick(1000, 500, 300, 1200, 520, 300, 500, 2, 2));
        Assert.IsFalse(ShellWindowFrame.IsCaptionDoubleClick(1000, 500, 300, 1200, 500, 340, 500, 2, 2));
        // 第一次按下（没有上一次）：不能算双击。
        Assert.IsFalse(ShellWindowFrame.IsCaptionDoubleClick(0, 0, 0, 1200, 500, 300, 500, 2, 2));
        // 取不到系统度量（0）时仍要有一个最小判定范围，不能把任何两次按下都当成双击。
        Assert.IsTrue(ShellWindowFrame.IsCaptionDoubleClick(1000, 500, 300, 1200, 500, 300, 500, 0, 0));
        Assert.IsFalse(ShellWindowFrame.IsCaptionDoubleClick(1000, 500, 300, 1200, 505, 300, 500, 0, 0));
    }

    private static int Hit(int x, int y, int dpi, bool maximized = false, WindowFrameRect? window = null)
        => ShellWindowFrame.HitTest(x, y, window ?? Window, dpi, maximized);
}
