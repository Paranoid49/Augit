using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class ScintillaControl
{
    private sealed record BlameRow(string Date, string Author, string Number, string Commit);
    private string _blameFontFamily = "Consolas";
    private double _blameFontSize = 13;
    private bool _blameDark, _drawingBlame;
    private nint _blameFont, _blameDc, _blameBitmap, _blamePreviousBitmap;
    private int _blameWidth, _blameDateWidth, _blameNumberWidth, _blameBufferWidth, _blameBufferHeight;

    internal int BlameWidthForTest => _blameWidth;
    internal int BlameDateWidthForTest => _blameDateWidth;
    internal int BlameNumberWidthForTest => _blameNumberWidth;
    internal int BlameAuthorWidthForTest { get; private set; }
    internal int BlameBufferAllocationsForTest { get; private set; }
    internal bool BlameDrawingAllocatedForTest => _blameFont != 0 || _blameDc != 0 || _blameBitmap != 0;
    internal (string Date, string Author, string Number) BlameRowForTest(int index)
    {
        BlameRow row = _blameRows[index];
        return (row.Date, row.Author, row.Number);
    }

    private void ConfigureBlameAppearance(string family, double size, bool dark)
    {
        _blameFontFamily = NativeFontResolver.ResolveMonospace(family);
        _blameFontSize = Math.Clamp(size, 9, 40);
        _blameDark = dark;
        if (_blameVisible) PrepareBlameFont();
    }

    private void PrepareBlameFont()
    {
        ReleaseBlameDrawing();
        _blameFont = NativeMethods.CreateFont(-(int)Math.Round(NativeTheme.Scale((float)_blameFontSize)),
            0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, _blameFontFamily);
        nint dc = NativeMethods.GetDeviceContext(Handle);
        nint previous = NativeMethods.SelectObject(dc, _blameFont);
        try
        {
            int Measure(string text)
            {
                NativeMethods.Rectangle bounds = default;
                _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds,
                    NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
                return bounds.Right;
            }
            // 保留稿中的 72/24/24px 三列；长日期、长行号或大字号按当前字体扩展。
            int dateLength = _blameRows.Values.Select(row => row.Date.Length).DefaultIfEmpty(9).Max();
            int numberLength = _blameRows.Values.Select(row => row.Number.Length).DefaultIfEmpty(3).Max();
            _blameDateWidth = Math.Max(NativeTheme.Scale(72), Measure(new string('8', dateLength)));
            _blameNumberWidth = Math.Max(NativeTheme.Scale(24), Measure(new string('8', numberLength)));
            int author = Math.Max(NativeTheme.Scale(24), Measure("I49"));
            BlameAuthorWidthForTest = author;
            // 列间距分别取整后求和，避免 150% 缩放时作者列被挤掉一物理像素。
            _blameWidth = _blameDateWidth + author + _blameNumberWidth
                + 2 * NativeTheme.Scale(6) + 2 * NativeTheme.Scale(5);
            _ = NativeMethods.SendMessage(Handle, SetMarginWidth, 0, _blameWidth);
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.ReleaseDeviceContext(Handle, dc);
        }
        InvalidateBlame();
    }

    private void InvalidateBlame()
    {
        if (!_blameVisible || Handle == 0) return;
        _ = NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle rectangle);
        rectangle.Right = Math.Min(rectangle.Right, _blameWidth);
        _ = NativeMethods.InvalidateRectangle(Handle, ref rectangle, false);
    }

    private void DrawBlameMargin(nint destination)
    {
        if (!_blameVisible || _blameFont == 0 || _drawingBlame || Handle == 0) return;
        bool release = destination == 0;
        if (release) destination = NativeMethods.GetDeviceContext(Handle);
        _drawingBlame = true;
        try
        {
            if (!NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client)) return;
            int width = Math.Min(client.Right, _blameWidth), height = client.Bottom;
            if (width <= 0 || height <= 0 || !PrepareBlameBuffer(destination, width, height)) return;
            NativeThemePalette palette = NativeTheme.Palette(_blameDark);
            nint previous = NativeMethods.SelectObject(_blameDc, _blameFont);
            try
            {
                Fill(new() { Right = width, Bottom = height }, palette.Panel);
                int first = (int)NativeMethods.SendMessage(Handle, 2221, (nuint)FirstVisibleLine, 0);
                int count = (int)NativeMethods.SendMessage(Handle, GetLineCount, 0, 0);
                int rowHeight = (int)NativeMethods.SendMessage(Handle, TextHeight, 0, 0);
                int padding = NativeTheme.Scale(6), gap = NativeTheme.Scale(5);
                for (int index = Math.Max(0, first); index < count; index++)
                {
                    nint position = NativeMethods.SendMessage(Handle, 2167, (nuint)index, 0);
                    int y = (int)NativeMethods.SendMessage(Handle, 2165, 0, position);
                    if (y >= height) break;
                    if (!_blameRows.TryGetValue(index, out BlameRow? row)) continue;
                    if (index == _selectedBlameLine) Fill(new() { Top = y, Right = width, Bottom = y + rowHeight }, palette.AccentSoft);
                    Text(row.Date, padding, padding + _blameDateWidth, y);
                    Text(row.Author, padding + _blameDateWidth + gap, _blameWidth - padding - _blameNumberWidth - gap, y);
                    Text(row.Number, _blameWidth - padding - _blameNumberWidth, _blameWidth - padding, y);
                }
                Fill(new() { Left = width - NativeTheme.Scale(1), Right = width, Bottom = height }, palette.Border);
                _ = CopyBlamePixels(destination, 0, 0, width, height, _blameDc, 0, 0, NativeMethods.RasterOperationSourceCopy);

                void Text(string value, int left, int right, int y)
                {
                    NativeMethods.Rectangle rectangle = new() { Left = left, Right = Math.Min(right, width), Top = y, Bottom = y + rowHeight };
                    _ = NativeMethods.SetBackgroundMode(_blameDc, NativeMethods.BackgroundModeTransparent);
                    _ = NativeMethods.SetTextColor(_blameDc, palette.Muted);
                    _ = NativeMethods.DrawText(_blameDc, value, value.Length, ref rectangle,
                        NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextVerticalCenter
                        | NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextEndEllipsis);
                }
                void Fill(NativeMethods.Rectangle rectangle, uint color)
                {
                    nint brush = NativeMethods.CreateSolidBrush(color);
                    _ = NativeMethods.FillRectangle(_blameDc, ref rectangle, brush);
                    _ = NativeMethods.DeleteObject(brush);
                }
            }
            finally { _ = NativeMethods.SelectObject(_blameDc, previous); }
        }
        finally
        {
            _drawingBlame = false;
            if (release) _ = NativeMethods.ReleaseDeviceContext(Handle, destination);
        }
    }

    private bool PrepareBlameBuffer(nint destination, int width, int height)
    {
        if (_blameDc != 0 && _blameBufferWidth == width && _blameBufferHeight == height) return true;
        ReleaseBlameBuffer();
        NativeMethods.BitmapInfo info = new()
        {
            Header = new() { Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 },
        };
        _blameBitmap = NativeMethods.CreateDeviceIndependentBitmap(0, ref info, 0, out _, 0, 0);
        _blameDc = NativeMethods.CreateCompatibleDeviceContext(destination);
        if (_blameBitmap == 0 || _blameDc == 0) { ReleaseBlameBuffer(); return false; }
        _blamePreviousBitmap = NativeMethods.SelectObject(_blameDc, _blameBitmap);
        BlameBufferAllocationsForTest++;
        _blameBufferWidth = width;
        _blameBufferHeight = height;
        return true;
    }

    private void ReleaseBlameBuffer()
    {
        if (_blameDc != 0)
        {
            if (_blamePreviousBitmap != 0) _ = NativeMethods.SelectObject(_blameDc, _blamePreviousBitmap);
            _ = NativeMethods.DeleteDeviceContext(_blameDc);
        }
        if (_blameBitmap != 0) _ = NativeMethods.DeleteObject(_blameBitmap);
        _blameDc = _blameBitmap = _blamePreviousBitmap = 0;
    }

    private void ReleaseBlameDrawing()
    {
        ReleaseBlameBuffer();
        if (_blameFont != 0) _ = NativeMethods.DeleteObject(_blameFont);
        _blameFont = 0;
    }

    [DllImport("gdi32.dll", EntryPoint = "BitBlt")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CopyBlamePixels(nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, uint operation);
}
