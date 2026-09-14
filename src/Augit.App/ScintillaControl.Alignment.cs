using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class ScintillaControl
{
    private const uint AnnotationSetText = 2540;
    private const uint AnnotationSetStyle = 2542;
    private const uint AnnotationClearAll = 2547;
    private const uint AnnotationSetVisible = 2548;
    private bool _hasDisplayGaps;

    internal void ClearDisplayGaps()
    {
        EnsureDecorationTarget();
        if (!_hasDisplayGaps) return;
        _ = SendDecorationMessage(AnnotationClearAll, 0, 0);
        _hasDisplayGaps = false;
    }

    internal void SetDisplayGapAfterLine(int line, int count)
    {
        EnsureDecorationTarget();
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        // Annotation 只占显示行，不属于正文、剪贴板文本或撤销记录。
        // 空格确保单行留白也有一行高度；换行数比所需显示行数少一。
        nint text = Marshal.StringToCoTaskMemUTF8(" " + new string('\n', count - 1));
        try
        {
            _ = SendDecorationMessage(AnnotationSetVisible, 1, 0);
            _ = SendDecorationMessage(AnnotationSetText, (nuint)line, text);
            _ = SendDecorationMessage(AnnotationSetStyle, (nuint)line, StyleDefault);
            _hasDisplayGaps = true;
        }
        finally { Marshal.FreeCoTaskMem(text); }
    }
}
