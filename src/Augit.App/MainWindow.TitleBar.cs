using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class MainWindow
{
    private const nuint FrameButtonSubclassIdentifier = 4;
    private static readonly NativeMethods.SubclassProcedure FrameButtonProcedure = HandleFrameButtonMessage;
    private nint _hoveredFrameButton;
    private bool _trackingFrameButtonMouseLeave;

    internal nint HoveredTitleBarButtonForTest => _hoveredFrameButton;
    internal bool TitleBarHoverTrackingForTest => _trackingFrameButtonMouseLeave;
    internal nint HoveredFrameButtonForTest => _hoveredFrameButton;
    internal bool FrameButtonHoverTrackingForTest => _trackingFrameButtonMouseLeave;

    private static bool IsFrameButtonCommand(uint identifier) => IsTitleBarCommand(identifier)
        || identifier is CommandFiles or CommandGitChanges or CommandWorkspaceSearch or CommandTerminal or CommandHistory
        or CommandLocateActiveFile or CommandCollapseTree or CommandTreeOptions or CommandHideProject
        or TerminalSessionCloseControlIdentifier or CommandTerminalMore or CommandHideTerminal;

    internal static void AttachFrameButton(nint parent, nint control)
    {
        if (!NativeMethods.SetWindowSubclass(control, FrameButtonProcedure, FrameButtonSubclassIdentifier, (nuint)parent))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), UiText.InterfaceControlCreateFailed);
    }

    private static void DrawFrameButtonFocus(NativeMethods.DrawItem item, NativeThemePalette palette, bool active)
    {
        if ((item.ItemState & NativeMethods.OwnerDrawFocus) == 0) return;
        NativeTheme.DrawToolbarFocus(item, palette);
        if (active)
        {
            // 蓝色激活底上的蓝色焦点框外加中性一像素边，避免两种状态融为一体。
            NativeMethods.Rectangle edge = item.ItemRectangle;
            edge.Left += S(1);
            edge.Top += S(1);
            edge.Right -= S(2);
            edge.Bottom -= S(2);
            // 用共同的 1px、2px 缩放边界，避免 150% DPI 下两条边重叠。
            int inset = Math.Max(1, S(2) - S(1));
            Fill(item.DeviceContext, new() { Left = edge.Left, Top = edge.Top, Right = edge.Right, Bottom = edge.Top + inset }, palette.Panel);
            Fill(item.DeviceContext, new() { Left = edge.Left, Top = edge.Bottom - inset, Right = edge.Right, Bottom = edge.Bottom }, palette.Panel);
            Fill(item.DeviceContext, new() { Left = edge.Left, Top = edge.Top, Right = edge.Left + inset, Bottom = edge.Bottom }, palette.Panel);
            Fill(item.DeviceContext, new() { Left = edge.Right - inset, Top = edge.Top, Right = edge.Right, Bottom = edge.Bottom }, palette.Panel);
        }
    }

    private static bool IsTitleBarCommand(uint identifier) => identifier is
        CommandMainMenu or CommandRecentWorkspaces or CommandBranch or CommandCurrentFile
        or CommandQuickOpen or CommandSettings or CommandMinimize or CommandMaximize or CommandCloseWindow;

    private static nint HandleFrameButtonMessage(nint window, uint message, nuint wordParameter,
        nint longParameter, nuint subclassIdentifier, nuint referenceData)
    {
        MainWindow? instance;
        lock (InstancesGate) Instances.TryGetValue((nint)referenceData, out instance);
        if (instance is not null && message == NativeMethods.WindowMessageKeyDown
            && wordParameter == NativeMethods.VirtualKeyEnter && NativeMethods.GetFocus() == window
            && NativeMethods.IsWindowVisible(window) && NativeMethods.IsWindowEnabled(window)
            && NativeMethods.GetKeyState(NativeMethods.VirtualKeyControl) >= 0
            && NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) >= 0
            && NativeMethods.GetKeyState(NativeMethods.VirtualKeyAlt) >= 0)
        {
            // Enter 沿用按钮点击；空格由原生 Button 处理，长按 Enter 不重复切换菜单。
            if (((long)longParameter & (1L << 30)) == 0)
                _ = NativeMethods.SendMessage(window, 0x00F5, 0, 0);
            return 0;
        }
        if (message == NativeMethods.WindowMessageNonClientDestroy)
        {
            instance?.ClearFrameButtonHover(window);
            _ = NativeMethods.RemoveWindowSubclass(window, FrameButtonProcedure, subclassIdentifier);
        }
        nint result = NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        if (instance is null) return result;
        if (message == NativeMethods.WindowMessageMouseMove)
        {
            int x = unchecked((short)(long)longParameter), y = unchecked((short)((long)longParameter >> 16));
            if (NativeMethods.IsWindowVisible(window) && NativeMethods.IsWindowEnabled(window)
                && NativeMethods.GetClientRectangle(window, out NativeMethods.Rectangle client)
                && x >= 0 && x < client.Right && y >= 0 && y < client.Bottom)
            {
                if (instance._hoveredFrameButton != window)
                {
                    instance.ClearFrameButtonHover(instance._hoveredFrameButton);
                    instance._hoveredFrameButton = window;
                    _ = NativeMethods.InvalidateRectangle(window, 0, false);
                }
                if (!instance._trackingFrameButtonMouseLeave)
                {
                    NativeMethods.TrackMouseEvent tracking = new()
                    {
                        Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                        Flags = NativeMethods.TrackMouseEventLeave,
                        Window = window,
                    };
                    instance._trackingFrameButtonMouseLeave = NativeMethods.TrackMouse(ref tracking);
                }
            }
            else instance.ClearFrameButtonHover(window);
        }
        else if (message == NativeMethods.WindowMessageMouseLeave
            || (message is NativeMethods.WindowMessageShowWindow or NativeMethods.WindowMessageEnable && wordParameter == 0))
            instance.ClearFrameButtonHover(window);
        else if (message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
            _ = NativeMethods.InvalidateRectangle(window, 0, false);
        return result;
    }

    private void ClearFrameButtonHover(nint window)
    {
        if (window == 0 || window != _hoveredFrameButton) return;
        if (_trackingFrameButtonMouseLeave)
        {
            NativeMethods.TrackMouseEvent tracking = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                Flags = NativeMethods.TrackMouseEventLeave | NativeMethods.TrackMouseEventCancel,
                Window = window,
            };
            _ = NativeMethods.TrackMouse(ref tracking);
        }
        _trackingFrameButtonMouseLeave = false;
        _hoveredFrameButton = 0;
        _ = NativeMethods.InvalidateRectangle(window, 0, false);
    }

    private bool DrawTitleBarButton(NativeMethods.DrawItem item, NativeThemePalette palette)
    {
        bool menuEntry = _mainMenuOpen && IsMainMenuButton(item.ControlIdentifier);
        bool disabled = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0;
        bool pressed = !disabled && (item.ItemState & NativeMethods.OwnerDrawSelected) != 0;
        // OwnerDraw 按钮不保证发送 ODS_HOTLIGHT，悬停使用本窗口的鼠标消息状态。
        bool hot = !disabled && item.Control == _hoveredFrameButton;
        bool highlighted = pressed || hot;
        bool workspace = !menuEntry && item.ControlIdentifier == CommandRecentWorkspaces;
        bool branch = !menuEntry && item.ControlIdentifier == CommandBranch;
        bool currentFile = !menuEntry && item.ControlIdentifier == CommandCurrentFile;
        bool closePressed = item.ControlIdentifier == CommandCloseWindow && pressed;
        uint background = closePressed ? Rgb(196, 43, 28)
            : workspace ? Blend(palette.Chrome, palette.Accent, highlighted ? 0.12 : 0.08)
            : highlighted ? Blend(palette.Chrome, branch ? palette.Accent : palette.Text,
                branch ? 0.12 : currentFile ? 0.07 : 0.08)
            : palette.Chrome;
        uint color = disabled ? palette.Faint : closePressed ? Rgb(255, 255, 255)
            : menuEntry || workspace || branch || currentFile || highlighted ? palette.Text : palette.Muted;
        Fill(item.DeviceContext, item.ItemRectangle, palette.Chrome);
        int radius = S(menuEntry ? 6 : 7);
        FillRounded(item.DeviceContext, item.ItemRectangle, background, radius * 2);
        if (!disabled && (item.ItemState & NativeMethods.OwnerDrawFocus) != 0)
        {
            // 焦点环位于命中区内，不改变布局，颜色和一像素宽度与视觉稿共用。
            FillRounded(item.DeviceContext, item.ItemRectangle, palette.Accent, radius * 2);
            NativeMethods.Rectangle inner = item.ItemRectangle;
            int inset = Math.Max(1, S(1));
            inner.Left += inset;
            inner.Top += inset;
            inner.Right -= inset;
            inner.Bottom -= inset;
            FillRounded(item.DeviceContext, inner, background, Math.Max(0, radius - inset) * 2);
        }
        string label = NativeMethods.GetWindowTextValue(item.Control);
        if (menuEntry)
        {
            NativeMethods.Rectangle labelRectangle = item.ItemRectangle;
            labelRectangle.Left += S(8);
            labelRectangle.Right -= S(8);
            DrawText(item.DeviceContext, label, labelRectangle, color, centered: false, fontWeight: NativeTheme.UiFont);
        }
        else if (workspace) DrawProductButton(item, label, color, disabled ? palette.Faint : palette.Accent);
        else if (branch) DrawBranchButton(item.DeviceContext, item.ItemRectangle, label, color);
        else if (currentFile) DrawCurrentFileButton(item.DeviceContext, item.ItemRectangle, GetCurrentFileContextText(), color);
        else DrawTopBarIcon(item.DeviceContext, item.ItemRectangle, (int)item.ControlIdentifier, color);
        return true;
    }
}
