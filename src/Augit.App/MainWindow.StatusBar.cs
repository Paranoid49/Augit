using Augit.Core.Documents;

namespace Augit.App;

internal sealed partial class MainWindow
{
    private readonly record struct StatusContext(string FullPath, string Location, string Encoding, string Ending, bool ReadOnly)
    {
        internal string Field(int index) => index switch { 0 => Encoding ?? string.Empty, 1 => Ending ?? string.Empty, _ => ReadOnly ? "只读" : string.Empty };
    }
    private StatusContext _statusContext;
    private string _statusAccessibleText = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _statusIsFileType;
    private readonly int[] _statusFieldWidths = new int[3];
    private int _statusLocationWidth;
    private int _statusMessageWidth;
    private int _statusBarUpdateCount;

    internal string StatusPathForTest => _statusContext.FullPath;
    internal string[] StatusFieldsForTest => StatusFields(_statusContext).Where(text => text.Length != 0).ToArray();
    internal nint StatusBarHandleForTest => _statusBar;
    internal int StatusBarUpdateCountForTest => _statusBarUpdateCount;

    private static string[] StatusFields(StatusContext context) => [context.Field(0), context.Field(1), context.Field(2)];

    private StatusContext GetStatusContext()
    {
        string root = _workspaceRoot ?? string.Empty;
        bool comparison = _showingReferenceComparison || _showingGitDiff;
        string? relativePath = _showingReferenceComparison ? _referenceComparisonDocument?.RelativePath
            : _showingGitDiff ? _gitDiffRelativePath : null;
        string? file = comparison
            ? string.IsNullOrWhiteSpace(relativePath) ? null : Path.GetFullPath(Path.Combine(root, relativePath))
            : _activeDocumentIndex >= 0 && _activeDocumentIndex < _documents.Count ? _documents[_activeDocumentIndex].Path : null;
        string fullPath = file ?? root;
        string location = root.Length == 0 ? UiText.AppName : Path.GetFileName(root);
        if (file is not null && root.Length != 0)
            location += "  ›  " + Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar.ToString(), "  ›  ", StringComparison.Ordinal);
        bool text = !comparison && file is not null && ActiveDocument?.Status == DocumentReadStatus.TextReady;
        string ending = text ? ActiveDocument!.LineEndings switch
        {
            DocumentLineEndings.Lf => "LF",
            DocumentLineEndings.CrLf => "CRLF",
            DocumentLineEndings.Cr => "CR",
            DocumentLineEndings.Mixed => "混合换行",
            DocumentLineEndings.None => "无换行",
            _ => string.Empty,
        } : string.Empty;
        return new(fullPath, location, text ? "UTF-8" : string.Empty, ending, comparison || file is not null);
    }

    private void UpdateStatusBar(bool forceMeasure = false)
    {
        if (_disposed || _statusBar == 0) return;
        StatusContext context = GetStatusContext();
        string message = _statusIsFileType ? string.Empty : _statusText;
        string[] fields = StatusFields(context);
        string accessible = string.Join(" · ", new[] { context.FullPath.Length == 0 ? context.Location : context.FullPath, message }
            .Concat(fields).Where(text => text.Length != 0));
        if (!forceMeasure && context == _statusContext && accessible == _statusAccessibleText) return;
        _statusBarUpdateCount++;
        _statusContext = context;
        _statusAccessibleText = accessible;
        _statusMessage = message;
        nint dc = NativeMethods.GetDeviceContext(_statusBar);
        try
        {
            _statusLocationWidth = MeasureTextWidth(dc, context.Location, NativeTheme.UiFont);
            _statusMessageWidth = MeasureTextWidth(dc, message, NativeTheme.UiFont);
            _statusFormatWidth = 0;
            for (int i = 0; i < fields.Length; i++)
            {
                _statusFieldWidths[i] = fields[i].Length == 0 ? 0 : MeasureTextWidth(dc, fields[i], NativeTheme.UiFont);
                if (_statusFieldWidths[i] != 0) _statusFormatWidth += _statusFieldWidths[i] + S(12);
            }
        }
        finally { _ = NativeMethods.ReleaseDeviceContext(_statusBar, dc); }
        _ = NativeMethods.SetWindowText(_statusBar, accessible);
        _toolTip?.Update(_statusBar, accessible);
        PositionStatusCancelButton();
        _ = NativeMethods.InvalidateRectangle(_statusBar, 0, false);
    }

    private void PositionStatusCancelButton()
    {
        if (_cancelBranchButton == 0 || !NativeMethods.GetClientRectangle(_handle, out var client)) return;
        _ = NativeMethods.SetWindowPosition(_cancelBranchButton, 0,
            Math.Max(0, client.Right - _statusFormatWidth - S(10) - _statusCancelWidth),
            Math.Max(0, client.Bottom - StatusHeight + S(1)), _statusCancelWidth, Math.Max(0, StatusHeight - S(2)),
            NativeMethods.SetWindowPositionNoZOrder | NativeMethods.SetWindowPositionNoActivate);
    }

    private void DrawStatusBar(NativeMethods.DrawItem item, uint background, uint color)
    {
        Fill(item.DeviceContext, item.ItemRectangle, background);
        NativeMethods.Rectangle rectangle = item.ItemRectangle;
        int right = rectangle.Right - S(6);
        for (int i = 2; i >= 0; i--)
        {
            if (_statusFieldWidths[i] == 0) continue;
            rectangle.Left = Math.Max(0, right - _statusFieldWidths[i]);
            rectangle.Right = right;
            DrawStatusText(item.DeviceContext, _statusContext.Field(i), rectangle, color);
            right = rectangle.Left - S(12);
        }
        if (_branchOperationCancellation is not null) right -= _statusCancelWidth + S(8);
        rectangle.Left = S(6);
        rectangle.Right = Math.Max(rectangle.Left, right);
        int available = rectangle.Right - rectangle.Left;
        int messageWidth = _statusMessage.Length == 0 ? 0 : Math.Min(_statusMessageWidth, available / 2);
        int gap = messageWidth == 0 ? 0 : S(12);
        int locationWidth = Math.Max(0, Math.Min(_statusLocationWidth, available - messageWidth - gap));
        rectangle.Right = rectangle.Left + locationWidth;
        DrawStatusText(item.DeviceContext, _statusContext.Location, rectangle, color);
        rectangle.Left = rectangle.Right + gap;
        rectangle.Right = right;
        if (messageWidth != 0) DrawStatusText(item.DeviceContext, _statusMessage, rectangle, color);
    }

    private static void DrawStatusText(nint dc, string text, NativeMethods.Rectangle rectangle, uint color)
    {
        if (rectangle.Right <= rectangle.Left || string.IsNullOrEmpty(text)) return;
        nint font = NativeMethods.SelectObject(dc, NativeTheme.UiFont);
        int mode = NativeMethods.SetBackgroundMode(dc, NativeMethods.BackgroundModeTransparent);
        uint previousColor = NativeMethods.SetTextColor(dc, color);
        try
        {
            _ = NativeMethods.DrawText(dc, text, text.Length, ref rectangle,
                NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextVerticalCenter | NativeMethods.DrawTextEndEllipsis | NativeMethods.DrawTextNoPrefix);
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, font);
            _ = NativeMethods.SetBackgroundMode(dc, mode);
            _ = NativeMethods.SetTextColor(dc, previousColor);
        }
    }
}
