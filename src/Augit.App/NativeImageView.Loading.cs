namespace Augit.App;

internal sealed partial class NativeImageView
{
    private void PaintLoading(nint dc, NativeMethods.Rectangle client)
    {
        if (!_loading) return;
        nint font = NativeMethods.SelectObject(dc, NativeTheme.UiFont);
        int previousMode = NativeMethods.SetBackgroundMode(dc, NativeMethods.BackgroundModeTransparent);
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        uint previousColor = NativeMethods.SetTextColor(dc, palette.Text);
        try
        {
            string text = UiText.ReadingFile;
            NativeMethods.Rectangle measured = new();
            _ = NativeMethods.DrawText(dc, text, text.Length, ref measured,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
            int padding = NativeTheme.Scale(12);
            int width = Math.Min(client.Right, measured.Right + padding * 2);
            int height = Math.Min(client.Bottom, measured.Bottom + padding * 2);
            NativeMethods.Rectangle box = new()
            {
                Left = (client.Right - width) / 2,
                Top = (client.Bottom - height) / 2,
                Right = (client.Right + width) / 2,
                Bottom = (client.Bottom + height) / 2,
            };
            _ = NativeGdiPlusDrawing.FillRoundedRectangle(dc, box, palette.Panel, NativeTheme.Scale(6));
            _ = NativeMethods.DrawText(dc, text, text.Length, ref box,
                NativeMethods.DrawTextCenter | NativeMethods.DrawTextVerticalCenter | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
        }
        finally
        {
            _ = NativeMethods.SetTextColor(dc, previousColor);
            _ = NativeMethods.SetBackgroundMode(dc, previousMode);
            _ = NativeMethods.SelectObject(dc, font);
        }
    }
}
