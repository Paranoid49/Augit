using System.Runtime.InteropServices;

namespace Augit.App;

/// <summary>
/// 统一原生选择框的主题表面；下拉、选择和键盘行为继续由 Windows 处理。
/// </summary>
internal static class NativeComboBoxTheme
{
    private const nuint SubclassIdentifier = 1;
    private const uint ComboEditState = 0x1000;
    private static readonly object Gate = new();
    private static readonly Dictionary<nint, Surface> Surfaces = [];
    private static readonly NativeMethods.SubclassProcedure Procedure = HandleMessage;

    private sealed class Surface(Func<bool> isDark, bool parentDrawsFrame)
    {
        internal readonly Func<bool> IsDark = isDark;
        internal readonly bool ParentDrawsFrame = parentDrawsFrame;
        internal int SelectedIndex = int.MinValue;
        internal string Label = string.Empty;
        internal NativeMethods.Rectangle TextBounds;
    }

    internal static uint ControlStyle => NativeMethods.ComboBoxDropDownList
        | NativeMethods.ComboBoxOwnerDrawFixed | NativeMethods.ComboBoxHasStrings;

    internal static bool Register(nint combo, Func<bool> isDark, int logicalItemHeight = 30, bool parentDrawsFrame = false)
    {
        if (combo == 0) return false;
        lock (Gate)
        {
            if (Surfaces.ContainsKey(combo)) return false;
            Surfaces.Add(combo, new(isDark, parentDrawsFrame));
        }
        if (!NativeMethods.SetWindowSubclass(combo, Procedure, SubclassIdentifier, 0))
        {
            lock (Gate) Surfaces.Remove(combo);
            return false;
        }
        _ = NativeMethods.SendMessage(combo, NativeMethods.ComboBoxSetItemHeight, unchecked((nuint)(-1)), NativeTheme.Scale(logicalItemHeight));
        _ = NativeMethods.SendMessage(combo, NativeMethods.ComboBoxSetItemHeight, 0, NativeTheme.Scale(logicalItemHeight));
        return true;
    }

    internal static void Unregister(nint combo)
    {
        if (combo == 0) return;
        _ = NativeMethods.RemoveWindowSubclass(combo, Procedure, SubclassIdentifier);
        lock (Gate) Surfaces.Remove(combo);
    }

    internal static int RegisteredCountForTest { get { lock (Gate) return Surfaces.Count; } }

    internal static NativeMethods.Rectangle TextBoundsForTest(nint combo)
    {
        lock (Gate) return Surfaces.TryGetValue(combo, out var surface) ? surface.TextBounds : default;
    }

    internal static string LabelForTest(nint combo)
    {
        lock (Gate) return Surfaces.TryGetValue(combo, out var surface) ? surface.Label : string.Empty;
    }

    internal static bool DrawItem(nint parameter, string[] labels, bool dark)
    {
        if (parameter == 0) return false;
        var item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        var palette = NativeTheme.Palette(dark);
        bool selected = (item.ItemState & ComboEditState) == 0 && (item.ItemState & NativeMethods.OwnerDrawSelected) != 0;
        NativeTheme.Fill(item.DeviceContext, item.ItemRectangle, selected ? palette.AccentSoft : palette.Panel);
        int index = unchecked((int)item.ItemIdentifier);
        if (index < 0 || index >= labels.Length)
            index = checked((int)NativeMethods.SendMessage(item.Control, NativeMethods.ComboBoxGetCurrentSelection, 0, 0));
        if (index < 0 || index >= labels.Length) return true;
        var text = item.ItemRectangle;
        text.Left += S(8); text.Right -= S(8);
        DrawText(item.DeviceContext, labels[index], text,
            (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0 ? palette.Faint : palette.Text);
        return true;
    }

    private static nint HandleMessage(nint window, uint message, nuint word, nint parameter, nuint id, nuint data)
    {
        if (message == NativeMethods.WindowMessageNonClientDestroy)
        {
            Unregister(window);
            return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
        }
        Surface? surface;
        lock (Gate) Surfaces.TryGetValue(window, out surface);
        if (surface is null) return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
        if (message is NativeMethods.WindowMessagePaint or 0x0317 or 0x0318)
        {
            if (message != NativeMethods.WindowMessagePaint) PaintSurface(window, unchecked((nint)word), surface);
            else
            {
                nint dc = NativeMethods.BeginPaint(window, out var paint);
                try { PaintSurface(window, dc, surface); }
                finally { _ = NativeMethods.EndPaint(window, ref paint); }
            }
            return 0;
        }
        if (message == NativeMethods.WindowMessageEraseBackground) return 1;
        nint result = NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
        // 下拉列表由系统拥有。只在现有选择或状态改变后重绘同一表面，不加计时器或轮询。
        if (message is 0x000C or 0x0143 or 0x0144 or 0x014A or 0x014B or 0x014E)
            surface.SelectedIndex = int.MinValue;
        if (message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus
            or NativeMethods.WindowMessageEnable or NativeMethods.WindowMessageKeyDown
            or NativeMethods.WindowMessageLeftButtonUp or 0x014E or 0x014F or 0x000C)
            _ = NativeMethods.InvalidateRectangle(window, 0, false);
        return result;
    }

    private static void PaintSurface(nint window, nint dc, Surface surface)
    {
        if (dc == 0 || !NativeMethods.GetClientRectangle(window, out var rect)) return;
        int index = checked((int)NativeMethods.SendMessage(window, NativeMethods.ComboBoxGetCurrentSelection, 0, 0));
        if (surface.SelectedIndex != index)
        {
            surface.Label = NativeMethods.GetWindowTextValue(window);
            surface.SelectedIndex = index;
        }
        surface.TextBounds = DrawClosedSurface(window, dc, surface.IsDark(), surface.Label, surface.ParentDrawsFrame);
    }

    internal static NativeMethods.Rectangle DrawClosedSurface(nint window, nint dc, bool dark, string label, bool parentDrawsFrame)
    {
        if (dc == 0 || !NativeMethods.GetClientRectangle(window, out var rect)) return default;
        var palette = NativeTheme.Palette(dark);
        bool enabled = NativeMethods.IsWindowEnabled(window);
        NativeTheme.Fill(dc, rect, palette.Panel);
        if (!parentDrawsFrame)
        {
            uint border = enabled && NativeMethods.GetFocus() == window ? palette.Accent : palette.Border;
            NativeTheme.FillRounded(dc, rect, border, S(10));
            var inner = rect;
            inner.Left += S(1); inner.Top += S(1); inner.Right -= S(1); inner.Bottom -= S(1);
            NativeTheme.FillRounded(dc, inner, palette.Panel, S(8));
        }
        // 父表单已绘制 1px 外框时，将文字内距减去这 1px，保持整体左右 8px。
        int inset = S(parentDrawsFrame ? 7 : 8);
        int centerX = rect.Right - S(parentDrawsFrame ? 12 : 13), centerY = rect.Bottom / 2;
        var text = rect; text.Left += inset; text.Right = Math.Max(text.Left, centerX - S(10));
        DrawText(dc, label, text, enabled ? palette.Text : palette.Faint);
        _ = NativeTheme.DrawChevronIcon(dc,
            new() { Left = centerX - S(8), Top = centerY - S(8), Right = centerX + S(8), Bottom = centerY + S(8) },
            expanded: true, enabled ? palette.Muted : palette.Faint);
        return text;
    }

    private static void DrawText(nint dc, string text, NativeMethods.Rectangle bounds, uint color)
    {
        nint previous = NativeMethods.SelectObject(dc, NativeTheme.UiFont);
        _ = NativeMethods.SetBackgroundMode(dc, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(dc, color);
        _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds,
            NativeMethods.DrawTextVerticalCenter | NativeMethods.DrawTextSingleLine
            | NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextEndEllipsis);
        _ = NativeMethods.SelectObject(dc, previous);
    }

    private static int S(int value) => NativeTheme.Scale(value);
}
