using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

/// <summary>
/// 显示完整文件回滚影响和真实 Diff 的确认窗口。
/// </summary>
internal sealed partial class NativeRollbackDialog : IDisposable
{
    private const string WindowClassName = "Augit.RollbackDialog.Native";
    private const int DialogWidth = 930;
    private const int DialogHeight = 430;
    private const int HeaderHeight = 45;
    private const int FooterHeight = 53;
    private const int CommandConfirm = 10;
    private const int CommandCancel = 11;
    private const int CommandClose = 12;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeRollbackDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitRepositorySnapshot _repository;
    private readonly GitChangedFile _file;
    private readonly IGitDiffService _diffService;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private nint _handle;
    private nint _noticeLabel;
    private nint _confirmButton;
    private nint _cancelButton;
    private nint _headerCloseButton;
    private nint _controlBrush;
    private NativeToolTip? _toolTip;
    private NativeDialogActionButtons? _buttons;
    private NativeGitComparisonView? _comparison;
    private bool _dark;
    private bool _recycle;
    private bool _confirmed;
    private bool _closed;
    private bool _disposed;
    private int? _quitCode;

    internal NativeRollbackDialog(
        nint owner,
        GitRepositorySnapshot repository,
        GitChangedFile file,
        GitDiffDocument document,
        IGitDiffService diffService,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        _owner = NativeMethods.GetAncestor(owner, NativeMethods.GetAncestorRoot);
        if (_owner == 0) _owner = owner;
        _repository = repository;
        _file = file;
        _diffService = diffService;
        _settings = settings;
        _setStatus = setStatus;
        EnsureWindowClass();
        (int x, int y) = Center(_owner, S(DialogWidth), S(DialogHeight));
        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.RollbackDialogTitle,
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleClipChildren,
            x,
            y,
            S(DialogWidth),
            S(DialogHeight),
            _owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.RollbackDialogCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        try { CreateControls(); ApplyAppearance(); ResizeToTypography(); SetComparisonResult(document); }
        catch { Dispose(); throw; }
    }

    internal static bool Show(
        nint owner,
        GitRepositorySnapshot repository,
        GitChangedFile file,
        GitDiffDocument document,
        IGitDiffService diffService,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        NativeRollbackDialog dialog = new(
            owner,
            repository,
            file,
            document,
            diffService,
            settings,
            setStatus);
        try { return dialog.Run(); }
        finally
        {
            dialog.Dispose();
            if (dialog._quitCode is { } code) NativeMethods.PostQuitMessage(code);
        }
    }

    internal static (int Width, int Height, int HeaderHeight, int FooterHeight) LogicalLayoutForTest =>
        (DialogWidth, DialogHeight, HeaderHeight, FooterHeight);

    internal static IReadOnlyList<string> WarningLabelsForTest =>
        [UiText.RollbackWarningTitle, UiText.RollbackWarningDetail, UiText.RollbackRecycleNotice];

    internal string FilePathForTest => _file.RelativePath;

    internal bool DiffPreviewCreatedForTest => _comparison is not null && _comparison.Handle != 0;

    internal nint HandleForTest => _handle;
    internal NativeGitComparisonView? ComparisonForTest => _comparison;
    internal nint HoveredButtonForTest => _buttons?.Hovered ?? 0;

    internal bool Run()
    {
        using NativeModalFocusScope focusScope = new(_owner);
        using NativeModalScrim scrim = NativeModalScrim.Begin(_owner, _dark);
        bool ownerEnabled = NativeMethods.IsWindowEnabled(_owner);
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        _ = NativeMethods.SetFocus(_cancelButton);
        try
        {
            while (!_closed)
            {
                int status = NativeMethods.GetMessage(out var message, 0, 0, 0);
                if (status <= 0)
                {
                    if (status == 0) _quitCode = unchecked((int)message.WordParameter);
                    break;
                }
                if (HandleKey(message)) continue;
                if (!NativeMethods.IsDialogMessage(_handle, ref message))
                {
                    _ = NativeMethods.TranslateMessage(ref message);
                    _ = NativeMethods.DispatchMessage(ref message);
                }
            }
        }
        finally
        {
            Dispose();
            if (NativeMethods.IsWindow(_owner))
            {
                _ = NativeMethods.EnableWindow(_owner, ownerEnabled);
                if (ownerEnabled) _ = NativeMethods.SetForegroundWindow(_owner);
            }
            focusScope.Restore();
        }
        return _confirmed;
    }

    internal bool HandleKey(NativeMethods.Message message)
    {
        if (_closed || message.MessageId != NativeMethods.WindowMessageKeyDown
            || !NativeFocusNavigation.ContainsWindow(_handle, message.Window)) return false;
        int key = unchecked((int)message.WordParameter);
        if (key == NativeMethods.VirtualKeyTab)
        {
            MoveFocus(NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0);
            return true;
        }
        if (key == NativeMethods.VirtualKeyEscape) { Close(); return true; }
        if (key != NativeMethods.VirtualKeyEnter) return false;
        if ((message.LongParameter.ToInt64() & (1L << 30)) != 0) return true;
        if (_comparison?.HandleShortcut(message) == true) return true;
        nint focus = NativeMethods.GetFocus();
        if (focus != _cancelButton && focus != _confirmButton && focus != _headerCloseButton) return false;
        if (NativeMethods.GetKeyState(NativeMethods.VirtualKeyControl) < 0
            || NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0 || NativeMethods.GetKeyState(0x12) < 0) return false;
        if (NativeMethods.IsWindowEnabled(focus) && NativeMethods.IsWindowVisible(focus))
            _ = NativeMethods.SendMessage(focus, 0x00F5, 0, 0);
        return true;
    }

    /// <summary>
    /// 回滚确认窗口先访问只读 Diff，再访问取消、确认和标题栏关闭动作。
    /// </summary>
    private void MoveFocus(bool backwards)
    {
        NativeFocusNavigation.MoveWithinRegion(
            [
                .. _comparison?.FocusTargets ?? [],
                _cancelButton,
                _confirmButton,
                _headerCloseButton,
            ],
            NativeMethods.GetFocus(),
            backwards);
        _bodyView?.EnsureVisible(NativeMethods.GetFocus());
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
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = NativeMethods.GetModuleHandle(null),
                Cursor = NativeMethods.LoadCursor(0, NativeMethods.ArrowCursor),
                Background = NativeMethods.GetSystemColorBrush(NativeMethods.ColorButtonFace),
                ClassName = WindowClassName,
            };
            ushort atom = NativeMethods.RegisterClass(ref windowClass);
            int error = Marshal.GetLastWin32Error();
            if (atom == 0 && error != NativeMethods.ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error, UiText.RollbackDialogClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeRollbackDialog? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        if (message is NativeMethods.WindowMessageControlColorButton
            or NativeMethods.WindowMessageControlColorStatic)
        {
            return instance.ApplyControlColor(unchecked((nint)wordParameter));
        }

        return message switch
        {
            NativeMethods.WindowMessagePaint => instance.PaintWindow(),
            NativeMethods.WindowMessageEraseBackground => 1,
            NativeMethods.WindowMessageNonClientHitTest => instance.HitTest(),
            NativeMethods.WindowMessageDrawItem => instance.DrawControl(longParameter) ? 1 : 0,
            NativeMethods.WindowMessageSize => instance.LayoutMessage(),
            NativeMethods.WindowMessageCommand => instance.CommandMessage(wordParameter, longParameter),
            NativeMethods.WindowMessageNonClientDestroy => instance.DestroyMessage(window, message, wordParameter, longParameter),
            NativeMethods.WindowMessageKeyDown when wordParameter == NativeMethods.VirtualKeyEscape => instance.CloseMessage(),
            NativeMethods.WindowMessageClose => instance.CloseMessage(),
            _ => NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter),
        };
    }

    private void CreateControls()
    {
        _recycle = _file.Group == GitChangeGroup.UnversionedFiles
            || _file.Kind is GitChangeKind.Added or GitChangeKind.Copied;
        _bodyView = new(_handle, MeasureBody, LayoutBody, PaintBody);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft, _bodyView.Handle);
        _bodyView.Register(_noticeLabel, true);
        _confirmButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.ConfirmRollbackAction,
            CommandConfirm,
            NativeMethods.ButtonOwnerDraw);
        _cancelButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.Cancel,
            CommandCancel,
            NativeMethods.ButtonOwnerDraw);
        _headerCloseButton = CreateControl(
            NativeMethods.ButtonClass,
            string.Empty,
            CommandClose,
            NativeMethods.ButtonOwnerDraw);
        _comparison = new(
            _bodyView.Handle,
            _settings,
            SetStatus,
            ReloadComparisonAsync);
        _toolTip = new NativeToolTip(_handle);
        _toolTip.Add(_headerCloseButton, UiText.Close);
        _toolTip.Add(_cancelButton, UiText.Cancel);
        _toolTip.Add(_confirmButton, UiText.ConfirmRollbackAction);
        _toolTip.Add(_handle, $"{UiText.RollbackDialogTitle} · {_file.RelativePath}");
        _buttons = new(_handle, _confirmButton, _cancelButton, _headerCloseButton);
        Layout();
    }

    private nint CreateControl(string className, string text, int identifier, uint style, nint parent = 0)
    {
        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleTabStop
                | style,
            0,
            0,
            0,
            0,
            parent == 0 ? _handle : parent,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.RollbackDialogControlCreateFailed);
        }

        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        return control;
    }

    private nint CommandMessage(nuint wordParameter, nint source)
    {
        if (_closed || source == 0 || NativeMethods.HighWord(wordParameter) != 0
            || !NativeMethods.IsWindowEnabled(source)) return 0;
        switch (NativeMethods.LowWord(wordParameter))
        {
            case CommandConfirm:
                _confirmed = true;
                Close();
                break;
            case CommandCancel:
            case CommandClose:
                Close();
                break;
        }

        return 0;
    }

    private nint CloseMessage()
    {
        Close();
        return 0;
    }

    private void SetComparisonResult(GitDiffDocument document)
    {
        if (_comparison is null)
        {
            return;
        }

        GitComparisonDocument comparison = new(
            document.Status,
            "HEAD",
            null,
            _file.RelativePath,
            document.UnifiedPatch);
        _comparison.SetResult(GitComparisonResult.Success(comparison));
        _comparison.SetVisible(true);
    }

    private async Task<GitComparisonResult> ReloadComparisonAsync(
        GitComparisonDocument _,
        bool ignoreWhitespace,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetimeCancellation.Token);
        GitDiffResult result = await _diffService.CreateAsync(
            _repository,
            _file,
            new(ignoreWhitespace),
            linked.Token).ConfigureAwait(true);
        if (!result.IsSuccess || result.Document is null)
        {
            return GitComparisonResult.Failure(
                result.FailureKind == GitOperationFailureKind.None
                    ? GitOperationFailureKind.CommandFailed
                    : result.FailureKind,
                result.ErrorMessage ?? UiText.GenerateDiffFailed);
        }

        return GitComparisonResult.Success(new(
            result.Document.Status,
            "HEAD",
            null,
            _file.RelativePath,
            result.Document.UnifiedPatch));
    }

    private void Layout()
    {
        if (_headerExtent == 0 || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = client.Right;
        int height = client.Bottom;
        int bodyTop = _headerExtent + S(1);
        Move(_bodyView?.Handle ?? 0, S(1), bodyTop, width - S(2), Math.Max(0, height - bodyTop - _footerExtent));
        _bodyView?.Relayout();
        int buttonTop = height - _footerExtent + (_footerExtent - _buttonExtent) / 2;
        Move(_confirmButton, width - S(17) - _confirmWidth, buttonTop, _confirmWidth, _buttonExtent);
        Move(_cancelButton, width - S(25) - _confirmWidth - _cancelWidth, buttonTop, _cancelWidth, _buttonExtent);
        Move(_headerCloseButton, width - S(45), (_headerExtent - S(31)) / 2, S(32), S(31));
        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
    }

    private void ApplyAppearance()
    {
        _dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, _dark);
        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(_dark).Panel);
        foreach (nint control in new[]
        {
            _bodyView?.Handle ?? 0,
            _noticeLabel,
            _confirmButton,
            _cancelButton,
            _headerCloseButton,
        })
        {
            NativeTheme.ApplyToControl(control, _dark);
        }

        _comparison?.ApplyAppearance();
        _toolTip?.ApplyAppearance(_dark);
    }

    private nint ApplyControlColor(nint deviceContext)
    {
        if (deviceContext == 0 || _controlBrush == 0)
        {
            return 0;
        }

        NativeThemePalette palette = NativeTheme.Palette(_dark);
        _ = NativeMethods.SetBackgroundColor(deviceContext, palette.Panel);
        _ = NativeMethods.SetTextColor(deviceContext, palette.Text);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        return _controlBrush;
    }

    private bool DrawControl(nint parameter)
    {
        if (parameter == 0)
        {
            return false;
        }

        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        return unchecked((int)item.ControlIdentifier) switch
        {
            CommandConfirm => _buttons?.Draw(item, _dark, NativeDialogActionButtons.Style.Danger) == true,
            CommandCancel => _buttons?.Draw(item, _dark, NativeDialogActionButtons.Style.Secondary) == true,
            CommandClose => _buttons?.Draw(item, _dark, NativeDialogActionButtons.Style.Close) == true,
            _ => false,
        };
    }

    private nint PaintWindow()
    {
        nint deviceContext = NativeMethods.BeginPaint(_handle, out NativeMethods.PaintStructure paint);
        if (deviceContext == 0)
        {
            return 0;
        }

        try
        {
            NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client);
            NativeThemePalette palette = NativeTheme.Palette(_dark);
            Fill(deviceContext, client, palette.BorderStrong);
            NativeTheme.FillRounded(deviceContext,
                new() { Left = S(1), Top = S(1), Right = client.Right - S(1), Bottom = client.Bottom - S(1) }, palette.Panel, S(16));
            Fill(deviceContext, new() { Left = 0, Top = _headerExtent, Right = client.Right, Bottom = _headerExtent + S(1) }, palette.Border);
            Fill(deviceContext, new() { Left = 0, Top = client.Bottom - _footerExtent, Right = client.Right, Bottom = client.Bottom - _footerExtent + S(1) }, palette.Border);
            DrawText(
                deviceContext,
                $"{UiText.RollbackDialogTitle} · {_file.RelativePath}",
                new() { Left = S(26), Top = S(1), Right = client.Right - S(60), Bottom = _headerExtent },
                palette.Text,
                NativeTheme.UiMediumFont);
        }
        finally
        {
            _ = NativeMethods.EndPaint(_handle, ref paint);
        }

        return 0;
    }

    private nint HitTest()
    {
        if (!NativeMethods.GetCursorPosition(out NativeMethods.Point point)
            || !NativeMethods.ScreenToClient(_handle, ref point))
        {
            return NativeMethods.HitTestClient;
        }

        _ = NativeMethods.GetClientRectangle(_handle, out var bounds);
        return point.Y < _headerExtent && point.X < bounds.Right - S(54)
            ? NativeMethods.HitTestCaption
            : NativeMethods.HitTestClient;
    }

    private nint LayoutMessage()
    {
        Layout();
        return 0;
    }

    private void SetStatus(string message)
    {
        if (_closed || _disposed) return;
        _noticeText = message;
        _ = NativeMethods.SetWindowText(_noticeLabel, message);
        _bodyView?.Relayout();
        if (message.Length > 0) _bodyView?.EnsureVisible(_noticeLabel);
        _setStatus(message);
    }

    private void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _lifetimeCancellation.Cancel();
        _bodyView?.Dispose();
        _bodyView = null;
        _buttons?.Dispose();
        _buttons = null;
        _comparison?.Dispose();
        _comparison = null;
        nint handle = _handle;
        NativeMethods.WakeWindowMessageLoop(handle);
        _handle = 0;
        lock (InstancesGate)
        {
            Instances.Remove(handle);
        }

        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
    }

    private nint DestroyMessage(nint window, uint message, nuint word, nint parameter)
    {
        Dispose();
        return NativeMethods.DefaultWindowProcedure(window, message, word, parameter);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _comparison?.Dispose();
        _comparison = null;
        _toolTip?.Dispose();
        _toolTip = null;
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }

        Close();
        _lifetimeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private static (int X, int Y) Center(nint owner, int width, int height)
    {
        if (!NativeMethods.GetWindowRectangle(owner, out NativeMethods.Rectangle rectangle))
        {
            return (NativeMethods.UseDefault, NativeMethods.UseDefault);
        }

        return (
            rectangle.Left + Math.Max(0, ((rectangle.Right - rectangle.Left) - width) / 2),
            rectangle.Top + Math.Max(0, ((rectangle.Bottom - rectangle.Top) - height) / 2));
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

    private static void DrawText(
        nint deviceContext,
        string text,
        NativeMethods.Rectangle rectangle,
        uint color,
        nint font)
    {
        _ = NativeMethods.SetTextColor(deviceContext, color);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        nint previous = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            text.Length,
            ref rectangle,
            NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextNoPrefix
                | NativeMethods.DrawTextEndEllipsis);
        if (previous != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previous);
        }
    }

    private static uint Rgb(byte red, byte green, byte blue)
    {
        return (uint)(red | green << 8 | blue << 16);
    }

    private static int S(int pixels) => NativeTheme.Scale(pixels);
}
