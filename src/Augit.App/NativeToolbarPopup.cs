using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed record NativeToolbarPopupItem(string Label, Action Action,
    Action<nint, NativeMethods.Rectangle, uint> Draw, bool Enabled);

/// <summary>
/// 竖向工具栏溢出时横向展示原有动作，复用图标、启用状态和中文辅助名称。
/// </summary>
internal sealed class NativeToolbarPopup : IDisposable
{
    private const string ClassName = "Augit.ToolbarPopup.Native";
    private static readonly object Gate = new();
    private static readonly Dictionary<nint, NativeToolbarPopup> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleMessage;
    private static readonly NativeMethods.SubclassProcedure ButtonProcedure = HandleButtonMessage;
    private static bool _registered;
    private readonly nint _owner;
    private readonly nint _previousFocus;
    private readonly IReadOnlyList<NativeToolbarPopupItem> _items;
    private readonly List<nint> _buttons = [];
    private readonly bool _dark;
    private NativeToolTip? _toolTip;
    private nint _handle;
    private bool _disposed;

    internal NativeToolbarPopup(nint owner, NativeMethods.Rectangle anchor,
        IReadOnlyList<NativeToolbarPopupItem> items, bool dark)
    {
        if (owner == 0 || items.Count == 0) throw new ArgumentException("工具栏弹层必须有所有者和动作。");
        _owner = owner;
        _previousFocus = NativeMethods.GetFocus();
        _items = items;
        _dark = dark;
        EnsureClass();
        int width = NativeTheme.Scale(items.Count * 32 + 8);
        int height = NativeTheme.Scale(36);
        (int x, int y) = NativeContextMenu.ConstrainToWorkArea(owner, anchor.Left, anchor.Top, width, height);
        try
        {
            _handle = NativeMethods.CreateWindow(NativeMethods.WindowExtendedStyleToolWindow,
                ClassName, string.Empty, NativeMethods.WindowStylePopup | NativeMethods.WindowStyleClipChildren,
                x, y, width, height, owner, 0, NativeMethods.GetModuleHandle(null), 0);
            if (_handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InterfaceControlCreateFailed);
            lock (Gate) Instances[_handle] = this;
            NativeTheme.ApplyToWindow(_handle, dark);
            nint region = NativeMethods.CreateRoundRectangleRegion(0, 0, width + 1, height + 1, NativeTheme.Scale(8), NativeTheme.Scale(8));
            if (region != 0) _ = NativeMethods.SetWindowRegion(_handle, region, true);
            _toolTip = new(_handle);
            for (int index = 0; index < items.Count; index++)
            {
                nint button = NativeMethods.CreateWindow(0, NativeMethods.ButtonClass, string.Empty,
                    NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.WindowStyleTabStop | NativeMethods.ButtonOwnerDraw,
                    NativeTheme.Scale(6 + index * 32), NativeTheme.Scale(4), NativeTheme.Scale(28), NativeTheme.Scale(28),
                    _handle, index + 1, NativeMethods.GetModuleHandle(null), 0);
                if (button == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InterfaceControlCreateFailed);
                _buttons.Add(button);
                NativeTheme.ApplyToControl(button, dark);
                _ = NativeMethods.EnableWindow(button, items[index].Enabled);
                _ = NativeMethods.SetWindowSubclass(button, ButtonProcedure, 1, unchecked((nuint)_handle));
                _toolTip.Add(button, items[index].Label);
            }
            _toolTip.ApplyAppearance(dark);
            _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowWithoutActivate);
            _ = NativeMethods.SetForegroundWindow(_handle);
            nint first = _buttons.FirstOrDefault(NativeMethods.IsWindowEnabled);
            _ = NativeMethods.SetFocus(first == 0 ? _handle : first);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal nint Handle => _handle;
    internal IReadOnlyList<nint> ButtonsForTest => _buttons;
    internal int ToolTipCountForTest => _toolTip?.CountForTest ?? 0;

    public void Dispose() => Close(restoreFocus: true);

    private void Close(bool restoreFocus)
    {
        if (_disposed) return;
        _disposed = true;
        _toolTip?.Dispose();
        _toolTip = null;
        foreach (nint button in _buttons) _ = NativeMethods.RemoveWindowSubclass(button, ButtonProcedure, 1);
        lock (Gate) Instances.Remove(_handle);
        nint foregroundBeforeDestroy = NativeMethods.GetForegroundWindow();
        nint root = NativeMethods.GetAncestor(_owner, NativeMethods.GetAncestorRoot);
        bool ownedForeground = foregroundBeforeDestroy == _handle || foregroundBeforeDestroy == _owner
            || NativeMethods.GetAncestor(foregroundBeforeDestroy, NativeMethods.GetAncestorRoot) == root;
        if (_handle != 0 && NativeMethods.IsWindow(_handle)) _ = NativeMethods.DestroyWindow(_handle);
        _handle = 0;
        _buttons.Clear();
        if (restoreFocus && NativeMethods.IsWindow(_previousFocus)
            && NativeMethods.IsWindowVisible(_previousFocus) && NativeMethods.IsWindowEnabled(_previousFocus))
        {
            nint foreground = NativeMethods.GetForegroundWindow();
            if (ownedForeground && (foreground == 0 || foreground == _owner
                || NativeMethods.GetAncestor(foreground, NativeMethods.GetAncestorRoot) == root))
            {
                if (foreground == 0 && root != 0 && NativeMethods.IsWindow(root))
                {
                    _ = NativeMethods.SetForegroundWindow(root);
                }
                _ = NativeMethods.SetFocus(_previousFocus);
            }
        }
    }

    private bool HandleKey(int key)
    {
        if (key == NativeMethods.VirtualKeyEscape)
        {
            Dispose();
            return true;
        }
        if (key is NativeMethods.VirtualKeyLeft or NativeMethods.VirtualKeyRight or NativeMethods.VirtualKeyTab)
        {
            bool backwards = key == NativeMethods.VirtualKeyLeft
                || key == NativeMethods.VirtualKeyTab && NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0;
            return NativeFocusNavigation.MoveWithinRegion(_buttons, NativeMethods.GetFocus(), backwards);
        }
        if (key is NativeMethods.VirtualKeyEnter or NativeMethods.VirtualKeySpace)
        {
            Activate(_buttons.IndexOf(NativeMethods.GetFocus()));
            return true;
        }
        return false;
    }

    private void Activate(int index)
    {
        if (index < 0 || index >= _items.Count || !_items[index].Enabled) return;
        Action action = _items[index].Action;
        Dispose();
        action();
    }

    private void Paint(nint dc, NativeMethods.Rectangle rectangle)
    {
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        Fill(dc, rectangle, palette.BorderStrong);
        rectangle.Left++;
        rectangle.Top++;
        rectangle.Right--;
        rectangle.Bottom--;
        Fill(dc, rectangle, palette.Panel);
    }

    private static void Fill(nint dc, NativeMethods.Rectangle rectangle, uint color)
    {
        nint brush = NativeMethods.CreateSolidBrush(color);
        if (brush == 0) return;
        _ = NativeMethods.FillRectangle(dc, ref rectangle, brush);
        _ = NativeMethods.DeleteObject(brush);
    }

    private static void EnsureClass()
    {
        lock (Gate)
        {
            if (_registered) return;
            NativeMethods.WindowClass windowClass = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(),
                Style = NativeMethods.ClassDropShadow,
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = NativeMethods.GetModuleHandle(null),
                Cursor = NativeMethods.LoadCursor(0, NativeMethods.ArrowCursor),
                ClassName = ClassName,
            };
            if (NativeMethods.RegisterClass(ref windowClass) == 0 && Marshal.GetLastWin32Error() != NativeMethods.ErrorClassAlreadyExists)
                throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.MainWindowClassRegisterFailed);
            _registered = true;
        }
    }

    private static nint HandleMessage(nint window, uint message, nuint word, nint parameter)
    {
        NativeToolbarPopup? popup;
        lock (Gate) Instances.TryGetValue(window, out popup);
        if (popup is null) return NativeMethods.DefaultWindowProcedure(window, message, word, parameter);
        switch (message)
        {
            case NativeMethods.WindowMessagePaint:
                nint dc = NativeMethods.BeginPaint(window, out NativeMethods.PaintStructure paint);
                try
                {
                    if (dc != 0 && NativeMethods.GetClientRectangle(window, out NativeMethods.Rectangle client)) popup.Paint(dc, client);
                }
                finally { _ = NativeMethods.EndPaint(window, ref paint); }
                return 0;
            case NativeMethods.WindowMessageEraseBackground:
                return 1;
            case NativeMethods.WindowMessageDrawItem:
                NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
                int index = unchecked((int)item.ControlIdentifier) - 1;
                if (index < 0 || index >= popup._items.Count) return 0;
                NativeThemePalette palette = NativeTheme.Palette(popup._dark);
                bool active = (item.ItemState & (NativeMethods.OwnerDrawFocus | NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0;
                bool enabled = popup._items[index].Enabled;
                Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
                if (enabled && active) _ = NativeGdiPlusDrawing.FillRoundedRectangle(item.DeviceContext, item.ItemRectangle, palette.AccentSoft, NativeTheme.Scale(5));
                popup._items[index].Draw(item.DeviceContext, item.ItemRectangle, enabled ? palette.Muted : palette.Faint);
                NativeTheme.DrawToolbarFocus(item, palette);
                return 1;
            case NativeMethods.WindowMessageCommand:
                popup.Activate(NativeMethods.LowWord(word) - 1);
                return 0;
            case NativeMethods.WindowMessageKeyDown:
                if (popup.HandleKey(unchecked((int)word))) return 0;
                break;
            case NativeMethods.WindowMessageActivate:
                if (NativeMethods.LowWord(word) == NativeMethods.WindowActivationInactive)
                {
                    popup.Close(restoreFocus: false);
                    return 0;
                }
                break;
            case NativeMethods.WindowMessageClose:
                popup.Dispose();
                return 0;
        }
        return NativeMethods.DefaultWindowProcedure(window, message, word, parameter);
    }

    private static nint HandleButtonMessage(nint window, uint message, nuint word, nint parameter, nuint id, nuint data)
    {
        NativeToolbarPopup? popup;
        lock (Gate) Instances.TryGetValue(unchecked((nint)data), out popup);
        if (message == NativeMethods.WindowMessageKeyDown && popup?.HandleKey(unchecked((int)word)) == true) return 0;
        return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
    }
}
