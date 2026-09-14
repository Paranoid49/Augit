using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

internal static partial class NativeGdiPlusDrawing
{
    // 使用系统文字排版处理中文断行、组合字符和字体回退；每块最多八行，绘制不再扫描整段。
    internal readonly record struct TextBlock(int Start, int Length, double Top, float Height, float Width);

    internal sealed class WrappedTextSession : IDisposable
    {
        private nint _graphics, _font, _format;
        internal float LineHeight { get; }

        internal WrappedTextSession(nint dc)
        {
            try
            {
                if (!EnsureStarted()) throw new Win32Exception("无法初始化提交说明绘制。");
                CheckTextStatus(GdipCreateFromHDC(dc, out _graphics));
                CheckTextStatus(GdipSetTextRenderingHint(_graphics, 5));
                CheckTextStatus(GdipCreateFontFromDC(dc, out _font));
                CheckTextStatus(GdipStringFormatGetGenericTypographic(out _format));
                // 不按每个块的浮点边界裁切字形；最终裁切仍由窗口客户区负责。
                CheckTextStatus(GdipSetStringFormatFlags(_format, 0x4000 | 0x2000 | 0x800 | 4));
                CheckTextStatus(GdipGetFontHeight(_font, _graphics, out float height));
                LineHeight = Math.Max(1, height);
            }
            catch { Dispose(); throw; }
        }

        internal TextBlock Measure(nint text, int start, int length, float width, double top, int maximumLines = 8)
        {
            TextRectangle rectangle = new() { Width = Math.Max(1, width), Height = LineHeight * maximumLines + 0.01f };
            CheckTextStatus(GdipMeasureString(_graphics, text + start * 2, length, _font, ref rectangle,
                _format, out TextRectangle measured, out int fitted, out _));
            return new(start, fitted, top, Math.Max(LineHeight, measured.Height), rectangle.Width);
        }

        internal void Draw(nint text, TextBlock block, float left, float top, uint color)
        {
            if (block.Length == 0) return;
            CheckTextStatus(GdipCreateSolidFill(ToArgb(color), out nint brush));
            try
            {
                TextRectangle rectangle = new()
                {
                    X = left,
                    Y = top,
                    Width = block.Width,
                    Height = block.Height + LineHeight,
                };
                CheckTextStatus(GdipDrawString(_graphics, text + block.Start * 2, block.Length,
                    _font, ref rectangle, _format, brush));
            }
            finally { _ = GdipDeleteBrush(brush); }
        }

        public void Dispose()
        {
            if (_format != 0) { _ = GdipDeleteStringFormat(_format); _format = 0; }
            if (_font != 0) { _ = GdipDeleteFont(_font); _font = 0; }
            if (_graphics != 0) { _ = GdipDeleteGraphics(_graphics); _graphics = 0; }
        }
    }

    private static void CheckTextStatus(int status)
    {
        if (status != GdiPlusOk) throw new Win32Exception($"提交说明排版失败（{status}）。");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextRectangle { internal float X, Y, Width, Height; }

    [DllImport("gdiplus.dll")]
    private static extern int GdipSetTextRenderingHint(nint graphics, int hint);
    [DllImport("gdiplus.dll")]
    private static extern int GdipCreateFontFromDC(nint dc, out nint font);
    [DllImport("gdiplus.dll")]
    private static extern int GdipDeleteFont(nint font);
    [DllImport("gdiplus.dll")]
    private static extern int GdipStringFormatGetGenericTypographic(out nint format);
    [DllImport("gdiplus.dll")]
    private static extern int GdipSetStringFormatFlags(nint format, int flags);
    [DllImport("gdiplus.dll")]
    private static extern int GdipDeleteStringFormat(nint format);
    [DllImport("gdiplus.dll")]
    private static extern int GdipGetFontHeight(nint font, nint graphics, out float height);
    [DllImport("gdiplus.dll")]
    private static extern int GdipMeasureString(nint graphics, nint text, int length, nint font,
        ref TextRectangle layout, nint format, out TextRectangle bounds, out int fitted, out int lines);
    [DllImport("gdiplus.dll")]
    private static extern int GdipDrawString(nint graphics, nint text, int length, nint font,
        ref TextRectangle layout, nint format, nint brush);
}
