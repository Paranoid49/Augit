using System.Runtime.InteropServices;
using System.Text;

namespace Augit.App;

internal sealed partial class ScintillaControl
{
    private const uint BeginUndoAction = 2078;
    private const uint EndUndoAction = 2079;
    private const uint SetTargetStart = 2190;
    private const uint SetTargetEnd = 2192;
    private const uint ReplaceTarget = 2194;

    // 只供已可编辑的结果区使用；按 UTF-8 字节范围替换，撤销记录仅包含当前冲突块。
    internal void ReplaceEditableRange(int start, int length, string replacement)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(replacement);
        if (IsReadOnly) throw new InvalidOperationException("只读正文不能执行编辑动作。");
        int currentLength = checked((int)NativeMethods.SendMessage(Handle, GetTextLength, 0, 0));
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, currentLength);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, currentLength - start);
        if (ClampUtf8Position(start, currentLength) != start || ClampUtf8Position(start + length, currentLength) != start + length)
            throw new ArgumentException("编辑范围必须位于完整 UTF-8 字符的边界。");

        int replacementLength = Encoding.UTF8.GetByteCount(replacement);
        int byteLength = checked(currentLength - length + replacementLength);
        int firstVisibleLine = FirstVisibleLine;
        int anchor = checked((int)NativeMethods.SendMessage(Handle, GetAnchor, 0, 0));
        int caret = checked((int)NativeMethods.SendMessage(Handle, GetCurrentPosition, 0, 0));
        nint xOffset = NativeMethods.SendMessage(Handle, GetXOffset, 0, 0);
        nint utf8 = Marshal.StringToCoTaskMemUTF8(replacement);
        _contentVersion++;
        _ = NativeMethods.SendMessage(Handle, BeginUndoAction, 0, 0);
        try
        {
            _ = NativeMethods.SendMessage(Handle, SetTargetStart, (nuint)start, 0);
            _ = NativeMethods.SendMessage(Handle, SetTargetEnd, (nuint)(start + length), 0);
            _ = NativeMethods.SendMessage(Handle, ReplaceTarget, (nuint)replacementLength, utf8);
        }
        finally
        {
            _ = NativeMethods.SendMessage(Handle, EndUndoAction, 0, 0);
            Marshal.FreeCoTaskMem(utf8);
        }

        // 未替换的正文继续跟随原字符；块内位置收敛到替换文本，保留反向选区与多字节边界。
        int MapPosition(int position) => ClampUtf8Position(position < start ? position
            : position >= start + length ? position - length + replacementLength
            : start + Math.Min(position - start, replacementLength), byteLength);
        _ = NativeMethods.SendMessage(Handle, SetSelection, (nuint)MapPosition(anchor), MapPosition(caret));
        _ = NativeMethods.SendMessage(Handle, SetFirstVisibleLineMessage, (nuint)Math.Max(0, firstVisibleLine), 0);
        _ = NativeMethods.SendMessage(Handle, SetXOffset, (nuint)xOffset, 0);
        UpdateLineNumberMargin();
        _viewportNeedsRefresh = true;
    }
}
