using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

internal enum NativeContextMenuIcon
{
    None,
    Copy,
    CherryPick,
    Compare,
    Reset,
    Revert,
    BranchPlus,
    Tag,
    Open,
    Terminal,
    Refresh,
    History,
    Blame,
    Search,
    Locate,
    Wrap,
    Whitespace,
    Close,
    Settings,
    Rename,
    Clone,
    Conflict,
}

internal sealed record NativeContextMenuItem(
    string Label,
    NativeContextMenuIcon Icon,
    Action Action,
    bool Enabled = true,
    bool Danger = false,
    bool Checked = false);

/// <summary>
/// 与视觉稿一致的轻量自绘上下文菜单。使用独立 Win32 弹层，避免系统菜单在不同主题和 DPI 下改变布局。
/// </summary>
internal sealed class NativeContextMenu : IDisposable
{
    private const string WindowClassName = "Augit.ContextMenu.Native";
    private const int MinimumPopupWidth = 200;
    private const int MaximumPopupWidth = 420;
    private const int PopupPadding = 8;
    private const int ItemHeight = 30;
    private const int SeparatorHeight = 13;
    private const int IconWidth = 20;
    private const int TextInset = 36;
    private const nuint WindowSubclassIdentifier = 1;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeContextMenu> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static readonly NativeMethods.SubclassProcedure SubclassProcedure = HandleSubclassMessage;
    private static bool _classRegistered;

    private readonly nint _owner;
    private readonly IReadOnlyList<NativeContextMenuItem?> _items;
    private readonly bool _dark;
    private readonly nint _focusBeforeOpen;
    private nint _handle;
    private int _hoveredIndex = -1;
    private int _selectedIndex = -1;
    private bool _disposed;

    private NativeContextMenu(
        nint owner,
        int anchorX,
        int anchorY,
        IReadOnlyList<NativeContextMenuItem?> items,
        bool dark)
    {
        _owner = owner;
        _items = items;
        _dark = dark;
        _focusBeforeOpen = NativeMethods.GetFocus();
        EnsureWindowClass();

        nint measureContext = NativeMethods.GetDeviceContext(owner);
        int width;
        try
        {
            width = MeasurePopupWidth(measureContext, items);
        }
        finally
        {
            _ = NativeMethods.ReleaseDeviceContext(owner, measureContext);
        }
        int height = CalculateHeight(items);
        (int x, int y) = ConstrainToWorkArea(owner, anchorX, anchorY, width, height);
        _handle = NativeMethods.CreateWindow(
            NativeMethods.WindowExtendedStyleToolWindow,
            WindowClassName,
            string.Empty,
            NativeMethods.WindowStylePopup
                | NativeMethods.WindowStyleClipChildren
                | NativeMethods.WindowStyleClipSiblings,
            x,
            y,
            width,
            height,
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InterfaceControlCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances[_handle] = this;
        }

        ApplyRoundedRegion(width, height);
        NativeTheme.ApplyToWindow(_handle, dark);
        _ = NativeMethods.SetWindowSubclass(_handle, SubclassProcedure, WindowSubclassIdentifier, 0);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowWithoutActivate);
        _ = NativeMethods.SetForegroundWindow(_handle);
        _ = NativeMethods.SetFocus(_handle);
        _selectedIndex = -1;
        _hoveredIndex = -1;
        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    internal nint Handle => _handle;

    internal IReadOnlyList<(string Label, NativeContextMenuIcon Icon)> VisualItemsForTest =>
        _items.Where(item => item is not null).Select(item => (item!.Label, item.Icon)).ToArray();

    internal static int MinimumPopupWidthForTest => S(MinimumPopupWidth);

    internal static int ItemHeightForTest => S(ItemHeight);

    internal static int SeparatorHeightForTest => S(SeparatorHeight);

    internal static int CalculatePopupTopForTest(int workAreaTop, int workAreaBottom, int anchorY, int height)
    {
        return CalculatePopupTop(workAreaTop, workAreaBottom, anchorY, height);
    }

    internal static int CalculateHeightForTest(int itemCount, int separatorCount)
    {
        return S(PopupPadding) * 2 + Math.Max(0, itemCount) * S(ItemHeight) + Math.Max(0, separatorCount) * S(SeparatorHeight);
    }

    internal static int FindNextSelectableIndexForTest(
        IReadOnlyList<bool> enabledItems,
        IReadOnlyList<bool> separators,
        int current,
        bool forwards)
    {
        int count = Math.Min(enabledItems.Count, separators.Count);
        if (count == 0)
        {
            return -1;
        }

        int step = forwards ? 1 : -1;
        int index = current;
        for (int attempt = 0; attempt < count; attempt++)
        {
            index = (index + step + count) % count;
            if (!separators[index] && enabledItems[index])
            {
                return index;
            }
        }

        return -1;
    }

    internal static NativeContextMenu Show(
        nint owner,
        int anchorX,
        int anchorY,
        IReadOnlyList<NativeContextMenuItem?> items,
        bool dark)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (owner == 0 || items.Count == 0)
        {
            throw new ArgumentException("上下文菜单必须有有效所有者和至少一个菜单项。", nameof(items));
        }

        return new NativeContextMenu(owner, anchorX, anchorY, items, dark);
    }

    public void Dispose()
    {
        Dispose(restoreFocus: true);
    }

    private void Dispose(bool restoreFocus)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != 0)
        {
            _ = NativeMethods.RemoveWindowSubclass(_handle, SubclassProcedure, WindowSubclassIdentifier);
        }

        lock (InstancesGate)
        {
            if (_handle != 0)
            {
                Instances.Remove(_handle);
            }
        }

        // 销毁弹层后系统可能立即清空前台窗口句柄，因此必须在销毁前记录
        // 弹层是否仍属于当前主窗口，关闭后才能可靠恢复原焦点。
        nint foregroundBeforeDestroy = NativeMethods.GetForegroundWindow();
        nint ownerRoot = NativeMethods.GetAncestor(_owner, NativeMethods.GetAncestorRoot);
        nint foregroundRootBeforeDestroy = NativeMethods.GetAncestor(
            foregroundBeforeDestroy,
            NativeMethods.GetAncestorRoot);
        bool foregroundBelongsToOwner = foregroundBeforeDestroy == _handle
            || foregroundBeforeDestroy == _owner
            || foregroundRootBeforeDestroy == ownerRoot;

        if (_handle != 0 && NativeMethods.IsWindow(_handle))
        {
            _ = NativeMethods.DestroyWindow(_handle);
        }

        _handle = 0;

        // 关闭弹层后回到打开前的控件；若用户已经切到其他应用，则不抢回外部焦点。
        if (restoreFocus
            && _focusBeforeOpen != 0
            && NativeMethods.IsWindow(_focusBeforeOpen)
            && NativeMethods.IsWindowVisible(_focusBeforeOpen)
            && NativeMethods.IsWindowEnabled(_focusBeforeOpen))
        {
            nint foreground = NativeMethods.GetForegroundWindow();
            if (foregroundBelongsToOwner && (foreground == 0 || foreground == _owner
                || NativeMethods.GetAncestor(foreground, NativeMethods.GetAncestorRoot) == ownerRoot))
            {
                if (foreground == 0 && ownerRoot != 0 && NativeMethods.IsWindow(ownerRoot))
                {
                    _ = NativeMethods.SetForegroundWindow(ownerRoot);
                }
                _ = NativeMethods.SetFocus(_focusBeforeOpen);
            }
        }
    }

    private static int S(int value) => NativeTheme.Scale(value);

    private static int CalculateHeight(IReadOnlyList<NativeContextMenuItem?> items)
    {
        int itemCount = items.Count(item => item is not null);
        int separatorCount = items.Count(item => item is null);
        // 与逐行绘制采用相同的缩放顺序，避免 125% DPI 下累计舍入吃掉底部留白。
        return S(PopupPadding) * 2 + itemCount * S(ItemHeight) + separatorCount * S(SeparatorHeight);
    }

    internal static int MeasurePopupWidth(nint deviceContext, IReadOnlyList<NativeContextMenuItem?> items)
    {
        nint previousFont = NativeMethods.SelectObject(deviceContext, NativeTheme.UiFont);
        int labelWidth = 0;
        try
        {
            foreach (NativeContextMenuItem? item in items)
            {
                if (item is null) continue;
                NativeMethods.Rectangle text = default;
                _ = NativeMethods.DrawText(deviceContext, item.Label, item.Label.Length, ref text,
                    NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
                labelWidth = Math.Max(labelWidth, text.Right - text.Left);
            }
        }
        finally
        {
            if (previousFont != 0) _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
        // 行背景左侧 4px、文字偏移、右侧文字留白 8px 与背景留白 4px。
        return Math.Clamp(labelWidth + S(TextInset + 16), S(MinimumPopupWidth), S(MaximumPopupWidth));
    }

    internal static (int X, int Y) ConstrainToWorkArea(
        nint owner,
        int anchorX,
        int anchorY,
        int width,
        int height)
    {
        // 菜单所有者经常是底部工具窗口等子窗口。使用顶层窗口的工作区边界，
        // 允许弹层跨越工具窗口与编辑区，不会被错误夹在子窗口矩形内。
        nint placementWindow = NativeMethods.GetAncestor(owner, NativeMethods.GetAncestorRoot);
        if (placementWindow == 0)
        {
            placementWindow = owner;
        }

        if (NativeMethods.GetWindowRectangle(placementWindow, out NativeMethods.Rectangle ownerRectangle))
        {
            int ownerX = Math.Clamp(
                anchorX,
                ownerRectangle.Left,
                Math.Max(ownerRectangle.Left, ownerRectangle.Right - width));
            int ownerY = CalculatePopupTop(ownerRectangle.Top, ownerRectangle.Bottom, anchorY, height);
            return (ownerX, ownerY);
        }

        nint monitor = NativeMethods.MonitorFromWindow(owner, NativeMethods.MonitorDefaultToNearest);
        NativeMethods.MonitorInfo info = new() { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return (anchorX, Math.Max(0, anchorY - height));
        }

        int x = Math.Clamp(anchorX, info.WorkArea.Left, Math.Max(info.WorkArea.Left, info.WorkArea.Right - width));
        int y = CalculatePopupTop(info.WorkArea.Top, info.WorkArea.Bottom, anchorY, height);
        return (x, y);
    }

    private static int CalculatePopupTop(int workAreaTop, int workAreaBottom, int anchorY, int height)
    {
        int candidate = anchorY + height <= workAreaBottom
            ? anchorY
            : anchorY - height;
        return Math.Clamp(
            candidate,
            workAreaTop,
            Math.Max(workAreaTop, workAreaBottom - height));
    }

    private static void EnsureWindowClass()
    {
        lock (ClassGate)
        {
            if (_classRegistered)
            {
                return;
            }

            NativeMethods.WindowClass windowClass = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(),
                Style = NativeMethods.ClassRedrawOnHorizontalChange
                    | NativeMethods.ClassRedrawOnVerticalChange
                    | NativeMethods.ClassDropShadow,
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = NativeMethods.GetModuleHandle(null),
                Cursor = NativeMethods.LoadCursor(0, NativeMethods.ArrowCursor),
                Background = NativeMethods.GetSystemColorBrush(NativeMethods.ColorWindow),
                ClassName = WindowClassName,
            };
            ushort atom = NativeMethods.RegisterClass(ref windowClass);
            int error = Marshal.GetLastWin32Error();
            if (atom == 0 && error != NativeMethods.ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error, UiText.MainWindowClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private void ApplyRoundedRegion(int width, int height)
    {
        nint region = NativeMethods.CreateRoundRectangleRegion(0, 0, width + 1, height + 1, S(16), S(16));
        if (region != 0)
        {
            if (NativeMethods.SetWindowRegion(_handle, region, true) == 0)
            {
                _ = NativeMethods.DeleteObject(region);
            }
        }
    }

    private int FindNextSelectableIndex(int current, bool forwards)
    {
        int step = forwards ? 1 : -1;
        int index = current;
        for (int attempt = 0; attempt < _items.Count; attempt++)
        {
            index = (index + step + _items.Count) % _items.Count;
            if (_items[index] is { Enabled: true })
            {
                return index;
            }
        }

        return -1;
    }

    private void ActivateSelected(int? hitIndex = null)
    {
        int index = hitIndex ?? (_hoveredIndex >= 0 ? _hoveredIndex : _selectedIndex);
        if (index < 0 || index >= _items.Count || _items[index] is not { Enabled: true } item)
        {
            return;
        }

        Dispose();
        item.Action();
    }

    private bool HitTest(int x, int y, out int index)
    {
        index = -1;
        if (!NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client)
            || x < S(4) || x >= client.Right - S(4))
        {
            return false;
        }
        int current = S(PopupPadding);
        for (int i = 0; i < _items.Count; i++)
        {
            int rowHeight = _items[i] is null ? S(SeparatorHeight) : S(ItemHeight);
            if (y >= current && y < current + rowHeight)
            {
                index = i;
                return _items[i] is not null;
            }

            current += rowHeight;
        }

        return false;
    }

    private void Paint()
    {
        nint deviceContext = NativeMethods.BeginPaint(_handle, out NativeMethods.PaintStructure paint);
        if (deviceContext == 0)
        {
            return;
        }

        try
        {
            NativeThemePalette palette = NativeTheme.Palette(_dark);
            if (!NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
            {
                return;
            }

            Fill(deviceContext, client, palette.BorderStrong);
            NativeMethods.Rectangle inner = client;
            inner.Left += S(1);
            inner.Top += S(1);
            inner.Right -= S(1);
            inner.Bottom -= S(1);
            FillRounded(deviceContext, inner, palette.Panel, S(14));
            int top = S(PopupPadding);
            for (int i = 0; i < _items.Count; i++)
            {
                NativeContextMenuItem? menuItem = _items[i];
                int rowHeight = menuItem is null ? S(SeparatorHeight) : S(ItemHeight);
                NativeMethods.Rectangle row = new()
                {
                    Left = S(4),
                    Top = top,
                    Right = Math.Max(S(4), client.Right - S(4)),
                    Bottom = top + rowHeight,
                };
                if (menuItem is null)
                {
                    NativeMethods.Rectangle separator = row;
                    separator.Left += S(4);
                    separator.Right -= S(4);
                    separator.Top += S(6);
                    separator.Bottom = separator.Top + S(1);
                    Fill(deviceContext, separator, palette.Border);
                }
                else
                {
                    bool active = i == _hoveredIndex || i == _selectedIndex;
                    if (active && menuItem.Enabled)
                    {
                        FillRounded(deviceContext, row, palette.AccentSoft, S(8));
                    }

                    NativeMethods.Rectangle icon = row;
                    icon.Left += S(8);
                    icon.Right = icon.Left + S(IconWidth);
                    if (menuItem.Checked)
                    {
                        _ = NativeTheme.DrawMenuCheckmark(
                            deviceContext,
                            icon,
                            menuItem.Enabled ? palette.Accent : palette.Faint);
                    }
                    else
                    {
                        DrawIcon(deviceContext, menuItem.Icon, icon, menuItem.Enabled ? palette.Muted : palette.Faint);
                    }

                    NativeMethods.Rectangle text = row;
                    text.Left += S(TextInset);
                    text.Right -= S(8);
                    DrawText(
                        deviceContext,
                        menuItem.Label,
                        text,
                        menuItem.Enabled
                            ? menuItem.Danger ? palette.Danger : palette.Text
                            : palette.Faint,
                        NativeTheme.UiFont);
                }

                top += rowHeight;
            }

        }
        finally
        {
            _ = NativeMethods.EndPaint(_handle, ref paint);
        }
    }

    private static void Fill(nint deviceContext, NativeMethods.Rectangle rectangle, uint color)
    {
        nint brush = NativeMethods.CreateSolidBrush(color);
        if (brush != 0)
        {
            _ = NativeMethods.FillRectangle(deviceContext, ref rectangle, brush);
            _ = NativeMethods.DeleteObject(brush);
        }
    }

    private static void FillRounded(nint deviceContext, NativeMethods.Rectangle rectangle, uint color, int diameter)
    {
        if (NativeGdiPlusDrawing.FillRoundedRectangle(deviceContext, rectangle, color, diameter))
        {
            return;
        }

        nint brush = NativeMethods.CreateSolidBrush(color);
        nint region = NativeMethods.CreateRoundRectangleRegion(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right + 1,
            rectangle.Bottom + 1,
            diameter,
            diameter);
        if (brush != 0 && region != 0)
        {
            _ = NativeMethods.FillRegion(deviceContext, region, brush);
        }

        if (region != 0)
        {
            _ = NativeMethods.DeleteObject(region);
        }

        if (brush != 0)
        {
            _ = NativeMethods.DeleteObject(brush);
        }
    }

    private static void DrawText(
        nint deviceContext,
        string text,
        NativeMethods.Rectangle rectangle,
        uint color,
        nint font)
    {
        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(deviceContext, color);
        uint format = NativeMethods.DrawTextSingleLine
            | NativeMethods.DrawTextVerticalCenter
            | NativeMethods.DrawTextNoPrefix
            | NativeMethods.DrawTextEndEllipsis;
        _ = NativeMethods.DrawText(deviceContext, text, text.Length, ref rectangle, format);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
    }

    internal static void DrawIcon(
        nint deviceContext,
        NativeContextMenuIcon icon,
        NativeMethods.Rectangle rectangle,
        uint color)
    {
        _ = NativeTheme.DrawMenuActionIcon(deviceContext, icon, rectangle, color);
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeContextMenu? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        switch (message)
        {
            case NativeMethods.WindowMessageActivate:
                if (NativeMethods.LowWord(wordParameter) == NativeMethods.WindowActivationInactive)
                {
                    // WM_ACTIVATE 已明确通知失活，此刻前台查询可能仍返回旧窗口。
                    instance.Dispose(restoreFocus: false);
                    return 0;
                }

                break;
            case NativeMethods.WindowMessagePaint:
                instance.Paint();
                return 0;
            case NativeMethods.WindowMessageEraseBackground:
                return 1;
            case NativeMethods.WindowMessageMouseMove:
                {
                    int x = unchecked((short)NativeMethods.LowWord(unchecked((nuint)longParameter)));
                    int y = unchecked((short)NativeMethods.HighWord(unchecked((nuint)longParameter)));
                    if (instance.HitTest(x, y, out int index))
                    {
                        instance._hoveredIndex = instance._selectedIndex = index;
                    }
                    else
                    {
                        instance._hoveredIndex = -1;
                    }

                    _ = NativeMethods.InvalidateRectangle(window, 0, false);
                    NativeMethods.TrackMouseEvent tracking = new()
                    {
                        Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                        Flags = NativeMethods.TrackMouseEventLeave,
                        Window = window,
                    };
                    _ = NativeMethods.TrackMouse(ref tracking);
                    return 0;
                }
            case NativeMethods.WindowMessageMouseLeave:
                instance._hoveredIndex = -1;
                _ = NativeMethods.InvalidateRectangle(window, 0, false);
                return 0;
            case NativeMethods.WindowMessageLeftButtonUp:
                {
                    int x = unchecked((short)NativeMethods.LowWord(unchecked((nuint)longParameter)));
                    int y = unchecked((short)NativeMethods.HighWord(unchecked((nuint)longParameter)));
                    if (instance.HitTest(x, y, out int index))
                    {
                        instance.ActivateSelected(index);
                    }
                    return 0;
                }
            case NativeMethods.WindowMessageKeyDown:
                switch (unchecked((int)wordParameter))
                {
                    case NativeMethods.VirtualKeyEscape:
                        instance.Dispose();
                        return 0;
                    case NativeMethods.VirtualKeyEnter:
                        instance.ActivateSelected();
                        return 0;
                    case NativeMethods.VirtualKeyUp:
                    case NativeMethods.VirtualKeyDown:
                        bool forwards = unchecked((int)wordParameter) == NativeMethods.VirtualKeyDown;
                        int next = instance.FindNextSelectableIndex(instance._selectedIndex, forwards);
                        if (next >= 0)
                        {
                            instance._selectedIndex = instance._hoveredIndex = next;
                            _ = NativeMethods.InvalidateRectangle(window, 0, false);
                        }

                        return 0;
                }

                break;
            case NativeMethods.WindowMessageClose:
                instance.Dispose();
                return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private static nint HandleSubclassMessage(
        nint window,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        _ = subclassIdentifier;
        _ = referenceData;
        return NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
    }
}
