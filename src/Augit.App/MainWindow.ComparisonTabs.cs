namespace Augit.App;

internal sealed partial class MainWindow
{
    internal bool CloseComparisonTabForTest(bool referenceComparison, bool closeButton,
        string? relativePath = null, Action? whilePressed = null, bool releaseOutside = false, bool cancelCapture = false)
    {
        if (!NativeMethods.GetClientRectangle(_documentTabs, out NativeMethods.Rectangle client)) return false;
        TransientEditorTabLayout? target = GetTransientTabLayouts(client.Right - client.Left)
            .Where(layout => (layout.Tab.Kind == TransientEditorTabKind.ReferenceComparison) == referenceComparison
                && (relativePath is null || layout.Tab.Key.Equals(relativePath, StringComparison.OrdinalIgnoreCase)))
            .Cast<TransientEditorTabLayout?>().FirstOrDefault();
        if (target is not { } tab) return false;
        int count = BuildTransientEditorTabs().Count;
        NativeMethods.Rectangle rectangle = tab.Rectangle;
        nint point = PackClientPoint(closeButton ? rectangle.Right - S(14) : (rectangle.Left + rectangle.Right) / 2,
            (rectangle.Top + rectangle.Bottom) / 2);
        if (!closeButton)
        {
            _ = NativeMethods.SendMessage(_documentTabs, NativeMethods.WindowMessageMiddleButtonUp, 0, point);
        }
        else
        {
            _ = NativeMethods.SendMessage(_documentTabs, NativeMethods.WindowMessageLeftButtonDown, 1, point);
            try
            {
                whilePressed?.Invoke();
                if (cancelCapture) _ = NativeMethods.ReleaseCapture();
                _ = NativeMethods.SendMessage(_documentTabs, NativeMethods.WindowMessageLeftButtonUp, 0,
                    releaseOutside ? PackClientPoint(-10, -10) : point);
            }
            finally
            {
                if (NativeMethods.GetCapture() == _documentTabs) _ = NativeMethods.ReleaseCapture();
            }
        }
        return BuildTransientEditorTabs().Count == count - 1;
    }
}
