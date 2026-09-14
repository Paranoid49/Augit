using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed partial class NativeResetDialog : IDisposable
{
    private const string WindowClassName = "Augit.ResetDialog.Native";
    private const int DialogWidth = 620;
    private const int DialogHeight = 288;
    private const int HeaderHeight = 45;
    private const int FooterHeight = 53;
    private const int CommandRun = 10;
    private const int CommandCancel = 11;
    private const int CommandClose = 12;
    private const int ControlTarget = 20;
    private const int ControlMode = 21;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeResetDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static readonly string[] RunLabels = [UiText.ConfirmResetHardAction, UiText.RunReset, UiText.RunningReset];
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitRepositorySnapshot _repository;
    private readonly IGitWorkspaceStateService _service;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly int? _trackedChangeCount;
    private readonly List<nint> _labels = [];
    private CancellationTokenSource? _operationCancellation;
    private nint _handle;
    private nint _body;
    private nint _targetEdit;
    private nint _modeCombo;
    private nint _runButton;
    private nint _cancelButton;
    private nint _headerCloseButton;
    private nint _noticeLabel;
    private nint _controlBrush;
    private NativeToolTip? _toolTip;
    private NativeDialogActionButtons? _buttons;
    private string _impactTitleText = string.Empty;
    private string _impactDetailText = string.Empty;
    private bool _dark;
    private bool _operationRunning;
    private bool _changed;
    private bool _closed;
    private int _fieldHeight;
    private int _contentHeight;
    private int _labelWidth;
    private int _alertHeight;
    private int _noticeHeight;
    private int _titleHeight, _detailHeight;
    private int _headerExtent, _footerExtent, _buttonExtent, _cancelWidth, _runWidth;
    private string _noticeText = string.Empty;
    private (int Width, int Line, string Title, string Detail, string Notice)? _bodyMeasureKey;

    internal NativeResetDialog(
        nint owner,
        GitRepositorySnapshot repository,
        IGitWorkspaceStateService service,
        ApplicationSettings settings,
        Action<string> setStatus,
        string? initialTarget,
        int? trackedChangeCount)
    {
        _owner = NativeMethods.GetAncestor(owner, NativeMethods.GetAncestorRoot);
        if (_owner == 0) _owner = owner;
        _repository = repository;
        _service = service;
        _settings = settings;
        _setStatus = setStatus;
        _trackedChangeCount = trackedChangeCount;
        EnsureWindowClass();
        (int x, int y) = Center(_owner, S(DialogWidth), S(DialogHeight));
        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.ResetTitle,
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.ResetDialogCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        try { CreateControls(initialTarget); ApplyAppearance(); ResizeToTypography(); }
        catch { Dispose(); throw; }
    }

    internal static bool Show(
        nint owner,
        GitRepositorySnapshot repository,
        IGitWorkspaceStateService service,
        ApplicationSettings settings,
        Action<string> setStatus,
        string? initialTarget = null,
        int? trackedChangeCount = null)
    {
        NativeResetDialog dialog = new(
            owner,
            repository,
            service,
            settings,
            setStatus,
            initialTarget,
            trackedChangeCount);
        try { return dialog.Run(); }
        finally
        {
            dialog.Dispose();
            if (dialog._quitCode is { } code) NativeMethods.PostQuitMessage(code);
        }
    }

    internal static (int Width, int Height, int HeaderHeight, int FooterHeight) LogicalLayoutForTest =>
        (DialogWidth, DialogHeight, HeaderHeight, FooterHeight);

    internal static IReadOnlyList<string> ModeDescriptionsForTest =>
        [UiText.ResetSoftDescription, UiText.ResetMixedDescription, UiText.ResetHardDescription];

    internal static IReadOnlyList<string> FieldLabelsForTest =>
        [UiText.ResetTarget, UiText.ResetMode];

    internal static IReadOnlyList<int> TabOrderForTest =>
        [ControlTarget, ControlMode, CommandCancel, CommandRun, CommandClose];

    internal nint BodyForTest => _body;
    internal nint NoticeForTest => _noticeLabel;
    internal int BodyMeasureCountForTest { get; private set; }
    internal nint[] FieldsForTest => [.. _labels, _targetEdit, _modeCombo];
    internal NativeMethods.Rectangle ModeTextBoundsForTest => NativeComboBoxTheme.TextBoundsForTest(_modeCombo);

    internal static uint ModeControlStyleForTest =>
        NativeMethods.ComboBoxDropDownList
        | NativeMethods.ComboBoxOwnerDrawFixed
        | NativeMethods.ComboBoxHasStrings;

    internal static (string Impact, string Detail, string Action, bool Danger) ModePresentationForTest(
        GitResetMode mode,
        int? trackedChangeCount = null)
    {
        return mode switch
        {
            GitResetMode.Soft => (
                UiText.ResetSoftImpact,
                UiText.ResetSoftNote,
                UiText.RunReset,
                false),
            GitResetMode.Mixed => (
                UiText.ResetMixedImpact,
                UiText.ResetMixedNote,
                UiText.RunReset,
                false),
            _ => (
                trackedChangeCount is int count
                    ? UiText.ResetHardImpactCount(count)
                    : UiText.ResetHardImpact,
                UiText.ResetHardNote,
                UiText.ConfirmResetHardAction,
                true),
        };
    }

    internal bool Run()
    {
        using NativeModalFocusScope focusScope = new(_owner);
        using NativeModalScrim scrim = NativeModalScrim.Begin(_owner, _dark);
        bool ownerEnabled = NativeMethods.IsWindowEnabled(_owner);
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        _ = NativeMethods.SetFocus(_targetEdit);
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
                if (_composing || !NativeMethods.IsDialogMessage(_handle, ref message))
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
        return _changed;
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
                throw new Win32Exception(error, UiText.ResetDialogClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeResetDialog? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        if (message is NativeMethods.WindowMessageControlColorEdit
            or NativeMethods.WindowMessageControlColorListBox
            or NativeMethods.WindowMessageControlColorButton
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
            OperationCompleted => instance.CompleteOperation(),
            NativeMethods.WindowMessageNonClientDestroy => instance.DestroyMessage(window, message, wordParameter, longParameter),
            NativeMethods.WindowMessageClose => instance.CloseMessage(),
            _ => NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter),
        };
    }

    private void CreateControls(string? initialTarget)
    {
        _bodyView = new(_handle, MeasureBody, LayoutBody, PaintBody, FocusFieldAt);
        _body = _bodyView.Handle;
        CreateLabel(UiText.ResetTarget);
        _targetEdit = CreateControl(
            NativeMethods.EditClass,
            string.IsNullOrWhiteSpace(initialTarget) ? "HEAD" : initialTarget,
            ControlTarget,
            NativeMethods.EditAutoHorizontalScroll,
            _body);
        CreateLabel(UiText.ResetMode);
        _modeCombo = CreateControl(
            NativeMethods.ComboBoxClass,
            string.Empty,
            ControlMode,
            ModeControlStyleForTest,
            _body);
        foreach (string mode in ModeDescriptionsForTest)
        {
            _ = NativeMethods.SendMessage(_modeCombo, NativeMethods.ComboBoxAddString, 0, mode);
        }

        _ = NativeMethods.SendMessage(
            _modeCombo,
            NativeMethods.ComboBoxSetItemHeight,
            unchecked((nuint)(-1)),
            S(28));
        _ = NativeMethods.SendMessage(
            _modeCombo,
            NativeMethods.ComboBoxSetItemHeight,
            0,
            S(28));
        _ = NativeMethods.SendMessage(_modeCombo, NativeMethods.ComboBoxSetCurrentSelection, 2, 0);
        if (!NativeComboBoxTheme.Register(_modeCombo, () => _dark, parentDrawsFrame: true))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.ResetDialogControlCreateFailed);
        }

        (_impactTitleText, _impactDetailText, _, _) = ModePresentationForTest(
            GitResetMode.Hard,
            _trackedChangeCount);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft, _body);
        _runButton = CreateControl(NativeMethods.ButtonClass, UiText.ConfirmResetHardAction, CommandRun, NativeMethods.ButtonOwnerDraw);
        _cancelButton = CreateControl(NativeMethods.ButtonClass, UiText.Cancel, CommandCancel, NativeMethods.ButtonOwnerDraw);
        _headerCloseButton = CreateControl(NativeMethods.ButtonClass, string.Empty, CommandClose, NativeMethods.ButtonOwnerDraw);
        _buttons = new(_handle, _runButton, _cancelButton, _headerCloseButton);
        _bodyView.Register(_targetEdit, true);
        _bodyView.Register(_modeCombo, true);
        _bodyView.Register(_noticeLabel, true);
        foreach (nint label in _labels) _bodyView.Register(label, true);
        _bodyView.Relayout();
        Layout();
        if (!NativeMethods.SetWindowSubclass(_targetEdit, InputProcedure, 1, unchecked((nuint)_handle)))
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.ResetDialogControlCreateFailed);
        _toolTip = new NativeToolTip(_handle);
        _toolTip.Add(_headerCloseButton, UiText.Close);
        _toolTip.Add(_cancelButton, UiText.Cancel);
        _toolTip.Add(_runButton, UiText.ConfirmResetHardAction);
        _toolTip.Add(_noticeLabel, string.Empty);
        Layout();
    }

    private void MoveFocus(bool backwards)
    {
        NativeFocusNavigation.MoveWithinRegion(
            [_targetEdit, _modeCombo, _cancelButton, _runButton, _headerCloseButton],
            NativeMethods.GetFocus(),
            backwards);
    }

    private NativeDialogBody? _bodyView;

    private void CreateLabel(string text)
    {
        _labels.Add(CreateControl(NativeMethods.StaticClass, text, 0, NativeMethods.StaticLeft, _body));
    }

    private nint CreateControl(string className, string text, int identifier, uint style, nint parent = 0)
    {
        uint commonStyle = NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible;
        if (!className.Equals(NativeMethods.StaticClass, StringComparison.Ordinal))
        {
            commonStyle |= NativeMethods.WindowStyleTabStop;
        }

        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            commonStyle | style,
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.ResetDialogControlCreateFailed);
        }

        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        return control;
    }

    private nint CommandMessage(nuint wordParameter, nint source)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (source == 0 || !NativeMethods.IsWindowEnabled(source) || _closed) return 0;
        if (command == ControlMode && source == _modeCombo && notification == NativeMethods.ComboBoxNotificationSelectionChanged)
        {
            UpdateModePresentation();
            return 0;
        }

        if (notification != 0) return 0;
        switch (command)
        {
            case CommandRun:
                StartReset();
                break;
            case CommandCancel:
                if (_operationRunning)
                {
                    _operationCancellation?.Cancel();
                }
                else
                {
                    Close();
                }
                break;
            case CommandClose:
                Close();
                break;
        }

        return 0;
    }

    private void StartReset()
    {
        if (_operationRunning || _closed)
        {
            return;
        }

        string target = NativeMethods.GetWindowTextValue(_targetEdit).Trim();
        if (target.Length == 0)
        {
            ShowError("请输入 Reset 目标提交。");
            _ = NativeMethods.SetFocus(_targetEdit);
            return;
        }

        _operationRunning = true;
        CancellationTokenSource cancellation = new();
        _operationCancellation = cancellation;
        SetOperationControlsEnabled(false);
        _ = NativeMethods.EnableWindow(_cancelButton, true);
        _ = NativeMethods.SetWindowText(_cancelButton, UiText.CancelOperation);
        _ = NativeMethods.SetWindowText(_runButton, UiText.RunningReset);
        _ = NativeMethods.InvalidateRectangle(_runButton, 0, true);
        _ = NativeMethods.SetFocus(_cancelButton);
        SetNotice(UiText.RunningReset);
        _operationTask = ExecuteResetAsync(target, SelectedMode, cancellation);
    }

    private GitResetMode SelectedMode => checked((int)NativeMethods.SendMessage(
        _modeCombo,
        NativeMethods.ComboBoxGetCurrentSelection,
        0,
        0)) switch
    {
        0 => GitResetMode.Soft,
        1 => GitResetMode.Mixed,
        _ => GitResetMode.Hard,
    };

    private void UpdateModePresentation()
    {
        (string impact, string detail, string action, _) = ModePresentationForTest(
            SelectedMode,
            _trackedChangeCount);
        _impactTitleText = impact;
        _impactDetailText = detail;
        _ = NativeMethods.SetWindowText(_runButton, action);
        _toolTip?.Update(_runButton, action);
        _ = NativeMethods.InvalidateRectangle(_runButton, 0, true);
        _ = NativeMethods.InvalidateRectangle(_modeCombo, 0, true);
        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
        _bodyView?.Relayout();
    }

    private void SetOperationControlsEnabled(bool enabled)
    {
        _ = NativeMethods.EnableWindow(_targetEdit, enabled);
        _ = NativeMethods.EnableWindow(_modeCombo, enabled);
        _ = NativeMethods.EnableWindow(_runButton, enabled);
        _ = NativeMethods.EnableWindow(_headerCloseButton, enabled);
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
        int bodyHeight = Math.Max(0, height - bodyTop - _footerExtent);
        Move(_body, S(1), bodyTop, width - S(2), bodyHeight);
        _bodyView?.Relayout();
        int buttonTop = height - _footerExtent + (_footerExtent - _buttonExtent) / 2;
        Move(_cancelButton, width - S(17) - _runWidth - S(8) - _cancelWidth, buttonTop, _cancelWidth, _buttonExtent);
        Move(_runButton, width - S(17) - _runWidth, buttonTop, _runWidth, _buttonExtent);
        Move(_headerCloseButton, width - S(45), (_headerExtent - S(31)) / 2, S(32), S(31));
        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    private void ResizeToTypography()
    {
        int line = NativeTheme.UiLineHeight;
        _headerExtent = Math.Max(S(HeaderHeight), line + S(16));
        _buttonExtent = Math.Max(S(30), line + S(10));
        _footerExtent = S(FooterHeight) + _buttonExtent - S(30);
        _cancelWidth = Math.Max(S(78), NativeDialogBody.Measure(_handle, UiText.CancelOperation).Width + S(26));
        _runWidth = Math.Max(S(116), RunLabels
            .Max(value => NativeDialogBody.Measure(_handle, value).Width) + S(26));
        int label = _labels.Max(control => NativeDialogBody.Measure(_handle, NativeMethods.GetWindowTextValue(control)).Width);
        int mode = ModeDescriptionsForTest.Max(value => NativeDialogBody.Measure(_handle, value).Width);
        _fieldHeight = Math.Max(S(30), line + S(10));
        _ = NativeMethods.SendMessage(_modeCombo, NativeMethods.ComboBoxSetItemHeight, unchecked((nuint)(-1)), _fieldHeight - S(6));
        _ = NativeMethods.SendMessage(_modeCombo, NativeMethods.ComboBoxSetItemHeight, 0, _fieldHeight - S(6));
        // 包含正文滚动条、选择框原生箭头及文字内距，避免限高时收起态裁掉末字。
        int naturalWidth = Math.Max(S(DialogWidth), Math.Max(label + mode + S(110), _cancelWidth + _runWidth + S(42)));
        if (!NativeMethods.GetWindowRectangle(_owner, out var owner)) return;
        int width = Math.Min(naturalWidth, Math.Max(1, owner.Right - owner.Left - S(90)));
        int height = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            height = Math.Min(Math.Max(S(DialogHeight), _headerExtent + MeasureBody(width - S(2)) + _footerExtent + S(1)),
                Math.Max(1, owner.Bottom - owner.Top - S(40)));
            _ = NativeMethods.SetWindowPosition(_handle, 0,
                owner.Left + (owner.Right - owner.Left - width) / 2,
                owner.Top + (owner.Bottom - owner.Top - height) / 2, width, height,
                NativeMethods.SetWindowPositionNoActivate | NativeMethods.SetWindowPositionNoZOrder);
            Layout();
            // 先让系统完成字段尺寸和滚动条布局，再以真实文字区域补足宽度。
            ComboBoxInfo combo = new() { Size = Marshal.SizeOf<ComboBoxInfo>() };
            if (pass != 0 || !GetComboBoxInfo(_modeCombo, ref combo)) break;
            int deficit = mode + S(16) - (combo.Item.Right - combo.Item.Left);
            if (deficit <= 0) break;
            width = Math.Min(width + deficit, Math.Max(1, owner.Right - owner.Left - S(90)));
        }
        nint region = NativeMethods.CreateRoundRectangleRegion(0, 0, width + 1, height + 1, S(18), S(18));
        if (region != 0 && NativeMethods.SetWindowRegion(_handle, region, true) == 0) _ = NativeMethods.DeleteObject(region);
        Layout();
    }

    private int MeasureBody(int width)
    {
        int line = NativeTheme.UiLineHeight;
        var key = (width, line, _impactTitleText, _impactDetailText, _noticeText);
        if (_bodyMeasureKey == key) return _contentHeight;
        _bodyMeasureKey = key;
        BodyMeasureCountForTest++;
        _fieldHeight = Math.Max(S(30), line + S(10));
        _labelWidth = Math.Max(S(110), FieldLabelsForTest.Max(label => NativeDialogBody.Measure(_handle, label).Width));
        int modeTop = S(15) + _fieldHeight + S(13);
        int alertTop = modeTop + _fieldHeight + S(16);
        int textWidth = Math.Max(S(120), width - S(34));
        _titleHeight = Math.Max(line, NativeDialogBody.Measure(_handle, _impactTitleText, textWidth - S(24), true).Height);
        _detailHeight = Math.Max(line, NativeDialogBody.Measure(_handle, _impactDetailText, textWidth - S(24)).Height);
        _alertHeight = Math.Max(S(62), _titleHeight + _detailHeight + S(16));
        _noticeHeight = _noticeText.Length == 0
            ? 0
            : Math.Max(line, NativeDialogBody.Measure(_handle, _noticeText, textWidth).Height);
        _contentHeight = alertTop + _alertHeight + (_noticeHeight == 0 ? S(17) : S(8) + _noticeHeight + S(17));
        _contentHeight = Math.Max(_contentHeight, S(180));
        return _contentHeight;
    }

    private void LayoutBody(int width, int height, int offset)
    {
        int left = S(17), right = Math.Max(left + S(120), width - S(17));
        int fieldLeft = left + _labelWidth + S(10);
        int targetTop = S(15) - offset;
        int modeTop = targetTop + _fieldHeight + S(13);
        Move(_labels[0], left, targetTop + (_fieldHeight - NativeTheme.UiLineHeight) / 2, _labelWidth, NativeTheme.UiLineHeight);
        Move(_labels[1], left, modeTop + (_fieldHeight - NativeTheme.UiLineHeight) / 2, _labelWidth, NativeTheme.UiLineHeight);
        Move(_targetEdit, fieldLeft + S(8), targetTop + (_fieldHeight - NativeTheme.UiLineHeight) / 2,
            right - fieldLeft - S(16), NativeTheme.UiLineHeight);
        Move(_modeCombo, fieldLeft + S(1), modeTop + S(1), right - fieldLeft - S(2), _fieldHeight - S(2));
        int alertTop = modeTop + _fieldHeight + S(16);
        Move(_noticeLabel, left, alertTop + _alertHeight + S(8), right - left, _noticeHeight);
        _ = NativeMethods.ShowWindow(_noticeLabel, _noticeHeight == 0 ? NativeMethods.ShowHide : NativeMethods.ShowNormal);
        _ = NativeMethods.InvalidateRectangle(_body, 0, false);
    }

    private void PaintBody(nint dc)
    {
        if (dc == 0 || !NativeMethods.GetClientRectangle(_body, out var client)) return;
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        Fill(dc, client, palette.Panel);
        int left = S(17), right = client.Right - S(17), fieldLeft = left + _labelWidth + S(10);
        int targetTop = S(15) - (_bodyView?.Offset ?? 0);
        int modeTop = targetTop + _fieldHeight + S(13);
        DrawFieldFrame(dc, new() { Left = fieldLeft, Top = targetTop, Right = right, Bottom = targetTop + _fieldHeight }, _targetEdit, palette);
        DrawFieldFrame(dc, new() { Left = fieldLeft, Top = modeTop, Right = right, Bottom = modeTop + _fieldHeight }, _modeCombo, palette);
        int alertTop = modeTop + _fieldHeight + S(16);
        uint alertColor = SelectedMode == GitResetMode.Hard ? (_dark ? Rgb(75, 45, 45) : Rgb(247, 215, 215)) : palette.AccentSoft;
        NativeMethods.Rectangle alert = new() { Left = left, Top = alertTop, Right = right, Bottom = alertTop + _alertHeight };
        NativeTheme.FillRounded(dc, alert, alertColor, S(10));
        DrawWrappedText(dc, _impactTitleText, new() { Left = left + S(12), Top = alertTop + S(6), Right = right - S(12), Bottom = alertTop + S(6) + _titleHeight }, palette.Text, NativeTheme.UiMediumFont);
        DrawWrappedText(dc, _impactDetailText, new() { Left = left + S(12), Top = alertTop + S(10) + _titleHeight, Right = right - S(12), Bottom = alertTop + S(10) + _titleHeight + _detailHeight }, palette.Muted, NativeTheme.UiFont);
    }

    private bool FocusFieldAt(int x, int y)
    {
        if (!NativeMethods.GetClientRectangle(_body, out var bounds)) return false;
        int left = S(17) + _labelWidth + S(10), right = bounds.Right - S(17);
        int top = S(15) - (_bodyView?.Offset ?? 0);
        if (x < left || x >= right) return false;
        nint control = y >= top && y < top + _fieldHeight ? _targetEdit
            : y >= top + _fieldHeight + S(13) && y < top + _fieldHeight * 2 + S(13) ? _modeCombo : 0;
        if (control == 0 || !NativeMethods.IsWindowEnabled(control)) return false;
        _ = NativeMethods.SetFocus(control);
        return true;
    }

    private static void DrawWrappedText(nint dc, string text, NativeMethods.Rectangle rectangle, uint color, nint font)
    {
        nint previous = NativeMethods.SelectObject(dc, font);
        _ = NativeMethods.SetBackgroundMode(dc, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(dc, color);
        _ = NativeMethods.DrawText(dc, text, text.Length, ref rectangle,
            NativeMethods.DrawTextWordBreak | NativeMethods.DrawTextNoPrefix);
        if (previous != 0) _ = NativeMethods.SelectObject(dc, previous);
    }

    private static void DrawFieldFrame(nint dc, NativeMethods.Rectangle frame, nint control, NativeThemePalette palette)
    {
        NativeTheme.FillRounded(dc, frame, NativeMethods.GetFocus() == control ? palette.Accent : palette.Border, NativeTheme.Scale(10));
        frame.Left += S(1); frame.Top += S(1); frame.Right -= S(1); frame.Bottom -= S(1);
        NativeTheme.FillRounded(dc, frame, palette.Panel, NativeTheme.Scale(8));
    }

    private static void Move(nint window, int x, int y, int width, int height)
    {
        _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
    }

    private void ApplyAppearance()
    {
        _dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, _dark);
        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(_dark).Panel);
        foreach (nint control in new[]
        {
            _body,
            _targetEdit,
            _modeCombo,
            _noticeLabel,
            _runButton,
            _cancelButton,
            _headerCloseButton,
        }.Concat(_labels))
        {
            NativeTheme.ApplyToControl(control, _dark);
        }

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
            CommandRun => _buttons?.Draw(item, _dark, SelectedMode == GitResetMode.Hard
                ? NativeDialogActionButtons.Style.Danger : NativeDialogActionButtons.Style.Primary) == true,
            CommandCancel => _buttons?.Draw(item, _dark, NativeDialogActionButtons.Style.Secondary) == true,
            CommandClose => _buttons?.Draw(item, _dark, NativeDialogActionButtons.Style.Close) == true,
            ControlMode => NativeComboBoxTheme.DrawItem(parameter, ModeDescriptionsForTest.ToArray(), _dark),
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
                new() { Left = S(1), Top = S(1), Right = client.Right - S(1), Bottom = client.Bottom - S(1) },
                palette.Panel, S(16));
            Fill(deviceContext, new() { Left = 0, Top = _headerExtent, Right = client.Right, Bottom = _headerExtent + S(1) }, palette.Border);
            Fill(deviceContext, new() { Left = 0, Top = client.Bottom - _footerExtent, Right = client.Right, Bottom = client.Bottom - _footerExtent + S(1) }, palette.Border);
            DrawText(deviceContext, UiText.ResetTitle, new() { Left = S(26), Top = S(1), Right = client.Right - S(60), Bottom = _headerExtent }, palette.Text, NativeTheme.UiMediumFont);
        }
        finally
        {
            _ = NativeMethods.EndPaint(_handle, ref paint);
        }

        return 0;
    }

    private nint HitTest()
    {
        if (!NativeMethods.GetCursorPosition(out NativeMethods.Point point) || !NativeMethods.ScreenToClient(_handle, ref point))
        {
            return NativeMethods.HitTestClient;
        }

        _ = NativeMethods.GetClientRectangle(_handle, out var bounds);
        return point.Y < _headerExtent && point.X < bounds.Right - S(54) ? NativeMethods.HitTestCaption : NativeMethods.HitTestClient;
    }

    private nint LayoutMessage()
    {
        Layout();
        return 0;
    }

    private nint CloseMessage()
    {
        Close();
        return 0;
    }

    private void ShowError(string message)
    {
        SetNotice(message);
        _setStatus(message);
    }

    private void SetNotice(string message)
    {
        if (_closed) return;
        _noticeText = message;
        _ = NativeMethods.SetWindowText(_noticeLabel, message);
        _toolTip?.Update(_noticeLabel, message);
        _bodyView?.Relayout();
        if (message.Length > 0) _bodyView?.EnsureVisible(_noticeLabel);
    }

    private void Close()
    {
        if (_closed)
        {
            return;
        }

        if (_operationRunning)
        {
            _operationCancellation?.Cancel();
            return;
        }

        Dispose();
    }

    public void Dispose()
    {
        nint handle;
        lock (_operationGate)
        {
            if (_closed) return;
            _closed = true;
            handle = _handle;
            _handle = 0;
            _operationCancellation?.Cancel();
            // 正在服务中的取消源由异步方法释放，不提前破坏取消回调注册。
            if (_pendingResult is not null) _operationCancellation?.Dispose();
            _pendingResult = null;
            _operationCancellation = null;
        }
        NativeMethods.WakeWindowMessageLoop(handle);
        _buttons?.Dispose();
        _buttons = null;
        _bodyView?.Dispose();
        _bodyView = null;
        if (_targetEdit != 0 && NativeMethods.IsWindow(_targetEdit))
            _ = NativeMethods.RemoveWindowSubclass(_targetEdit, InputProcedure, 1);
        NativeComboBoxTheme.Unregister(_modeCombo);
        _modeCombo = 0;
        lock (InstancesGate)
        {
            Instances.Remove(handle);
        }

        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
        _toolTip?.Dispose();
        _toolTip = null;
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }

        GC.SuppressFinalize(this);
    }

    private static (int X, int Y) Center(nint owner, int width, int height)
    {
        if (!NativeMethods.GetWindowRectangle(owner, out NativeMethods.Rectangle rectangle))
        {
            return (NativeMethods.UseDefault, NativeMethods.UseDefault);
        }

        return (rectangle.Left + Math.Max(0, ((rectangle.Right - rectangle.Left) - width) / 2), rectangle.Top + Math.Max(0, ((rectangle.Bottom - rectangle.Top) - height) / 2));
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

    private static void DrawText(nint deviceContext, string text, NativeMethods.Rectangle rectangle, uint color, nint font)
    {
        _ = NativeMethods.SetTextColor(deviceContext, color);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        nint previous = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.DrawText(deviceContext, text, text.Length, ref rectangle, NativeMethods.DrawTextVerticalCenter | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
        if (previous != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previous);
        }
    }

    private static uint Rgb(byte red, byte green, byte blue)
    {
        return (uint)(red | (green << 8) | (blue << 16));
    }

    private static int S(int pixels) => NativeTheme.Scale(pixels);

    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo
    {
        internal int Size;
        internal NativeMethods.Rectangle Item, Button;
        internal uint ButtonState;
        internal nint Combo, Edit, List;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetComboBoxInfo(nint combo, ref ComboBoxInfo info);

}
