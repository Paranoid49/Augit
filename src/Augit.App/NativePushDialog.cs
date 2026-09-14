using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed partial class NativePushDialog : IDisposable
{
    private const string WindowClassName = "Augit.PushDialog.Native";
    private const int DialogWidth = 930;
    // 宽双栏 Push 视觉稿的常规总高度：45 标题栏 + 365 内容区 + 53 操作栏，含分隔线。
    private const int DialogHeight = 494;
    // 无远端状态额外显示“推送标签”行，按视觉稿向下增加 30 像素。
    private const int NoRemoteDialogHeight = 524;
    private const int HeaderHeight = 45;
    private const int FooterHeight = 53;
    private const int SidebarWidth = 260;
    // 无远端状态的引用行沿用视觉稿中的内联布局：分支摘要后紧跟“定义远端”。
    private const int SummaryTextOffset = 16;
    private const int DefineRemoteOffset = 70;
    private const int DefineRemoteWidth = 72;
    private const int CommitListIdentifier = 1;
    private const int CommandPush = 10;
    private const int CommandCancel = 11;
    private const int CommandDefineRemote = 12;
    private const int CommandClose = 13;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativePushDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static readonly string[] PushTagLabels = [UiText.AllTags];
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitRepositorySnapshot _repository;
    private readonly IGitRemoteService _service;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly string? _localReference;
    private readonly List<GitPushCommitPreview> _commits = [];
    private GitPushPreview? _preview;
    private CancellationTokenSource? _operationCancellation;
    private nint _handle;
    private string _summaryText = UiText.ReadingPushPreview;
    private nint _detailTitleLabel;
    private nint _targetLabel;
    private nint _credentialsLabel;
    private nint _emptyStateLabel;
    private nint _pushTagsCheck;
    private nint _pushTagsCombo;
    private nint _commitList;
    private nint _noticeLabel;
    private nint _pushButton;
    private nint _defineRemoteButton;
    private nint _cancelButton;
    private nint _headerCloseButton;
    private nint _controlBrush;
    private NativeToolTip? _toolTip;
    private bool _dark;
    private bool _operationRunning;
    private volatile bool _previewLoadCompleted;
    private bool _closed;
    private bool _noRemoteLayout;
    private bool _restoreFocusAfterPreview;
    private int _headerHeight;
    private int _footerHeight;
    private int _rowHeight;
    private int _buttonHeight;
    private int _contentHeight;
    private int _bodyInset;
    private int _dialogWidth;
    private int _dialogHeight;
    private int _noRemoteDialogHeight;
    private int _sidebarWidth;
    private int _defineRemoteLeft;
    private int _defineRemoteWidth;
    private int _pushTagsWidth;
    private int _pushTagsComboWidth;
    private int _cancelButtonWidth;
    private int _pushButtonWidth;

    internal NativePushDialog(
        nint owner,
        GitRepositorySnapshot repository,
        IGitRemoteService service,
        ApplicationSettings settings,
        Action<string> setStatus,
        string? localReference = null)
    {
        _owner = NativeMethods.GetAncestor(owner, NativeMethods.GetAncestorRoot);
        if (_owner == 0) _owner = owner;
        _repository = repository;
        _service = service;
        _settings = settings;
        _setStatus = setStatus;
        _localReference = localReference;
        _detailProcedure = HandleDetailMessage;
        _commitProcedure = HandleCommitListMessage;
        EnsureWindowClass();
        _headerHeight = NativeTheme.ContentHeight(HeaderHeight, 18);
        _footerHeight = NativeTheme.ContentHeight(FooterHeight, 25);
        _rowHeight = NativeTheme.ContentHeight(27, 8);
        _buttonHeight = NativeTheme.ContentHeight(28, 8);
        _contentHeight = S(365);
        _bodyInset = S(15);
        _dialogWidth = S(DialogWidth);
        _dialogHeight = _headerHeight + (_bodyInset * 2) + _contentHeight + _footerHeight + S(1);
        _noRemoteDialogHeight = _dialogHeight + Math.Max(S(30), _rowHeight + S(3));
        _sidebarWidth = S(SidebarWidth);
        _defineRemoteLeft = S(DefineRemoteOffset);

        (int x, int y) = Center(owner, _dialogWidth, _dialogHeight);
        _handle = NativeMethods.CreateWindow(0, WindowClassName, UiText.PushDialogTitle, NativeMethods.WindowStylePopup | NativeMethods.WindowStyleClipChildren, x, y, _dialogWidth, _dialogHeight, owner, 0, NativeMethods.GetModuleHandle(null), 0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.PushDialogCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        try { CreateControls(); ApplyAppearance(); ApplyDialogLayoutMode(false, force: true); StartPreview(); }
        catch { Dispose(); throw; }
    }

    internal static void Show(
        nint owner,
        GitRepositorySnapshot repository,
        IGitRemoteService service,
        ApplicationSettings settings,
        Action<string> setStatus,
        string? localReference = null)
    {
        NativePushDialog dialog = new(owner, repository, service, settings, setStatus, localReference);
        try { dialog.Run(); }
        finally
        {
            dialog.Dispose();
            if (dialog._quitCode is { } code) NativeMethods.PostQuitMessage(code);
        }
    }

    internal static (int Width, int Height, int HeaderHeight, int FooterHeight) LogicalLayoutForTest =>
        (DialogWidth, DialogHeight, HeaderHeight, FooterHeight);

    internal static string WindowClassNameForTest => WindowClassName;

    internal static bool IsPreviewLoadCompletedForTest(nint handle)
    {
        lock (InstancesGate)
        {
            return Instances.TryGetValue(handle, out NativePushDialog? dialog)
                && dialog._previewLoadCompleted;
        }
    }

    internal static (int HorizontalInset, int VerticalInset, int SidebarWidth, int ContentHeight) ContentLayoutForTest =>
        (17, 15, SidebarWidth, 365);

    internal static int DialogHeightForStateForTest(bool noRemote) =>
        noRemote ? NoRemoteDialogHeight : DialogHeight;

    internal static (int Header, int Footer, int Row, int Button, int Dialog, int NoRemoteDialog)
        CalculateAdaptiveMetricsForTest(int lineHeight)
    {
        int line = Math.Max(1, lineHeight);
        int header = Math.Max(HeaderHeight, line + 18);
        int footer = Math.Max(FooterHeight, line + 25);
        int row = Math.Max(27, line + 8);
        int button = Math.Max(28, line + 8);
        int dialog = header + 30 + 365 + footer + 1;
        return (header, footer, row, button, dialog, dialog + Math.Max(30, row + 3));
    }

    internal static (int SummaryOffset, int DefineRemoteOffset, int DefineRemoteWidth) NoRemoteInlineActionLayoutForTest =>
        (SummaryTextOffset, DefineRemoteOffset, DefineRemoteWidth);

    internal static IReadOnlyList<string> DisabledTagOptionsForTest => [UiText.PushTags, UiText.AllTags];

    internal void Run()
    {
        using NativeModalFocusScope focusScope = new(_owner);
        using NativeModalScrim scrim = NativeModalScrim.Begin(_owner, _dark);
        bool ownerEnabled = NativeMethods.IsWindowEnabled(_owner);
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        _ = NativeMethods.SetFocus(_commitList);
        try
        {
            while (!Volatile.Read(ref _closed))
            {
                int status = NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0);
                if (status <= 0)
                {
                    if (status == 0) _quitCode = unchecked((int)message.WordParameter);
                    break;
                }
                if (message.MessageId == NativeMethods.WindowMessageKeyDown
                    && NativeFocusNavigation.ContainsWindow(_handle, message.Window)
                    && unchecked((int)message.WordParameter) == NativeMethods.VirtualKeyTab)
                {
                    MoveFocus(NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0);
                    continue;
                }

                if (message.MessageId == NativeMethods.WindowMessageKeyDown
                    && NativeFocusNavigation.ContainsWindow(_handle, message.Window)
                    && unchecked((int)message.WordParameter) == NativeMethods.VirtualKeyEscape)
                {
                    Close();
                    continue;
                }

                if (message.MessageId == NativeMethods.WindowMessageKeyDown
                    && NativeFocusNavigation.ContainsWindow(_handle, message.Window)
                    && message.WordParameter == NativeMethods.VirtualKeyEnter)
                {
                    nint focus = NativeMethods.GetFocus();
                    if (focus == _cancelButton || focus == _headerCloseButton) Close();
                    else if (focus == _defineRemoteButton) DefineRemote();
                    else StartPush();
                    continue;
                }

                if (!NativeMethods.IsDialogMessage(_handle, ref message))
                {
                    _ = NativeMethods.TranslateMessage(ref message);
                    _ = NativeMethods.DispatchMessage(ref message);
                }
            }
        }
        finally
        {
            if (NativeMethods.IsWindow(_owner))
            {
                _ = NativeMethods.EnableWindow(_owner, ownerEnabled);
                focusScope.Restore();
            }
        }
    }

    /// <summary>
    /// 按 Push 视觉稿在提交列表、辅助动作和底部动作之间循环移动焦点。
    /// 不可见或禁用的动作会被自动跳过。
    /// </summary>
    private void MoveFocus(bool backwards)
    {
        NativeFocusNavigation.MoveWithinRegion(
            [
                _commitList,
                _detailPanel,
                _pushTagsCheck,
                _pushTagsCombo,
                _defineRemoteButton,
                _cancelButton,
                _pushButton,
                _headerCloseButton,
            ],
            NativeMethods.GetFocus(),
            backwards);
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
                throw new Win32Exception(error, UiText.PushDialogClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativePushDialog? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        if (message == NativeMethods.WindowMessageDrawItem)
        {
            return instance.DrawControl(longParameter) ? 1 : 0;
        }

        if (message is NativeMethods.WindowMessageControlColorListBox
            or NativeMethods.WindowMessageControlColorButton
            or NativeMethods.WindowMessageControlColorStatic)
        {
            return instance.ApplyControlColor(unchecked((nint)wordParameter), longParameter);
        }

        return message switch
        {
            NativeMethods.WindowMessagePaint => instance.PaintWindow(),
            NativeMethods.WindowMessageEraseBackground => 1,
            NativeMethods.WindowMessageNonClientHitTest => instance.HitTest(),
            NativeMethods.WindowMessageSize => instance.LayoutMessage(),
            NativeMethods.WindowMessageCommand => instance.CommandMessage(wordParameter),
            NativeMethods.WindowMessageClose => instance.CloseMessage(),
            PreviewCompleted => instance.CompletePreview(),
            PushCompleted => instance.CompletePush(),
            NativeMethods.WindowMessageNonClientDestroy => instance.DestroyedMessage(),
            _ => NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter),
        };
    }

    private void CreateControls()
    {
        _detailPanel = CreateControl(NativeMethods.StaticClass, string.Empty, 30, NativeMethods.WindowStyleVerticalScroll | NativeMethods.WindowStyleClipChildren | NativeMethods.WindowStyleTabStop);
        if (!NativeMethods.SetWindowSubclass(_detailPanel, _detailProcedure, 1, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.PushDialogControlCreateFailed);
        _detailTitleLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 31, NativeMethods.StaticLeft, _detailPanel);
        _targetLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 32, NativeMethods.StaticLeft, _detailPanel);
        _credentialsLabel = CreateControl(NativeMethods.StaticClass, UiText.PushCredentialsNotice, 33, NativeMethods.StaticLeft, _detailPanel);
        _emptyStateLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 34, NativeMethods.StaticCenter, _detailPanel);
        _pushTagsCheck = CreateControl(NativeMethods.ButtonClass, UiText.PushTags, 20, NativeMethods.ButtonAutoCheckbox);
        _pushTagsCombo = CreateControl(NativeMethods.ComboBoxClass, string.Empty, 21, NativeComboBoxTheme.ControlStyle);
        _ = NativeMethods.SendMessage(_pushTagsCombo, NativeMethods.ComboBoxAddString, 0, UiText.AllTags);
        _ = NativeMethods.SendMessage(_pushTagsCombo, NativeMethods.ComboBoxSetCurrentSelection, 0, 0);
        int comboLogicalHeight = Math.Max(
            30,
            (int)Math.Ceiling(NativeTheme.UiLineHeight * 96d / Math.Max(1, NativeTheme.ActiveDpiForTest)) + 8);
        if (!NativeComboBoxTheme.Register(_pushTagsCombo, () => _dark, comboLogicalHeight))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.PushDialogControlCreateFailed);
        }
        _ = NativeMethods.EnableWindow(_pushTagsCheck, false);
        _ = NativeMethods.EnableWindow(_pushTagsCombo, false);
        _commitList = CreateControl(NativeMethods.ListBoxClass, string.Empty, CommitListIdentifier,
            NativeMethods.WindowStyleVerticalScroll | NativeMethods.ListBoxNoIntegralHeight | NativeMethods.ListBoxHasStrings | NativeMethods.ListBoxOwnerDrawFixed);
        if (!NativeMethods.SetWindowSubclass(_commitList, _commitProcedure, 1, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.PushDialogControlCreateFailed);
        _ = NativeMethods.SendMessage(_commitList, NativeMethods.ListBoxSetItemHeight, 0, _rowHeight);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 35, NativeMethods.StaticLeft, _detailPanel);
        _defineRemoteButton = CreateControl(NativeMethods.ButtonClass, UiText.DefineRemote, CommandDefineRemote, NativeMethods.ButtonOwnerDraw);
        _cancelButton = CreateControl(NativeMethods.ButtonClass, UiText.Cancel, CommandCancel, NativeMethods.ButtonOwnerDraw);
        _pushButton = CreateControl(NativeMethods.ButtonClass, UiText.Push, CommandPush, NativeMethods.ButtonOwnerDraw);
        _headerCloseButton = CreateControl(NativeMethods.ButtonClass, UiText.CloseSymbol, CommandClose, NativeMethods.ButtonOwnerDraw);
        _toolTip = new NativeToolTip(_handle);
        _toolTip.Add(_headerCloseButton, UiText.Close);
        _toolTip.Add(_detailPanel, "推送详情，可滚动阅读");
        _toolTip.Add(_noticeLabel, UiText.Push);
        _toolTip.Add(_targetLabel, UiText.Push);
        _ = NativeMethods.ShowWindow(_defineRemoteButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_emptyStateLabel, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_pushTagsCheck, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_pushTagsCombo, NativeMethods.ShowHide);
        _ = NativeMethods.EnableWindow(_pushButton, false);
        _defineRemoteWidth = Math.Max(S(DefineRemoteWidth), MeasureTextWidth(UiText.DefineRemote, NativeTheme.UiFont) + S(16));
        _pushTagsWidth = Math.Max(S(108), MeasureTextWidth(UiText.PushTags, NativeTheme.UiFont) + S(30));
        _pushTagsComboWidth = Math.Max(S(96), MeasureTextWidth(UiText.AllTags, NativeTheme.UiFont) + S(56));
        _cancelButtonWidth = Math.Max(S(78), MeasureTextWidth(UiText.CancelOperation, NativeTheme.UiFont) + S(24));
        _pushButtonWidth = Math.Max(S(84), MeasureTextWidth(UiText.Pushing, NativeTheme.UiFont) + S(24));
        Layout();
    }

    private nint CreateControl(string className, string text, int identifier, uint style, nint parent = 0)
    {
        uint commonStyle = NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible;
        if (!className.Equals(NativeMethods.StaticClass, StringComparison.Ordinal))
        {
            commonStyle |= NativeMethods.WindowStyleTabStop;
        }

        nint control = NativeMethods.CreateWindow(0, className, text, commonStyle | style, 0, 0, 0, 0, parent == 0 ? _handle : parent, identifier, NativeMethods.GetModuleHandle(null), 0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.PushDialogControlCreateFailed);
        }

        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        if (parent == _detailPanel && parent != 0 && !NativeMethods.SetWindowSubclass(control, _detailProcedure, 1, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.PushDialogControlCreateFailed);
        return control;
    }

    private nint CommandMessage(nuint wordParameter)
    {
        if (_closed || NativeMethods.HighWord(wordParameter) != 0) return 0;
        switch (NativeMethods.LowWord(wordParameter))
        {
            case CommandPush:
                StartPush();
                break;
            case CommandDefineRemote:
                DefineRemote();
                break;
            case CommandCancel:
                if (_operationRunning)
                {
                    RequestCancellation();
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

    private void ApplyPreview(GitPushPreviewResult result)
    {
        _detailMeasureWidth = 0;
        if (!result.IsSuccess || result.Preview is null)
        {
            _preview = null;
            _commits.Clear();
            _ = NativeMethods.SendMessage(_commitList, NativeMethods.ListBoxResetContent, 0, 0);
            string requestedName = ShortReference(result.LocalReference ?? _localReference) ?? UiText.CurrentBranch;
            _summaryText = result.CanDefineRemote ? $"{requestedName} →" : requestedName;
            _ = NativeMethods.SetWindowText(_detailTitleLabel, string.Empty);
            _ = NativeMethods.SetWindowText(_targetLabel, string.Empty);
            _ = NativeMethods.SetWindowText(_credentialsLabel, string.Empty);
            _ = NativeMethods.SetWindowText(
                _emptyStateLabel,
                result.CanDefineRemote
                    ? UiText.PushNoSelection
                    : result.ErrorMessage ?? UiText.GitUnavailable);
            _ = NativeMethods.ShowWindow(_emptyStateLabel, NativeMethods.ShowNormal);
            _ = NativeMethods.ShowWindow(
                _defineRemoteButton,
                result.CanDefineRemote ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
            _ = NativeMethods.ShowWindow(
                _pushTagsCheck,
                result.CanDefineRemote ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
            _ = NativeMethods.ShowWindow(
                _pushTagsCombo,
                result.CanDefineRemote ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
            _ = NativeMethods.EnableWindow(_pushButton, false);
            SetNotice(string.Empty);
            _previewLoadCompleted = true;
            UpdateNoRemoteLayoutMetrics();
            ApplyDialogLayoutMode(result.CanDefineRemote);
            Layout();
            _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
            return;
        }

        GitPushPreview preview = result.Preview;
        _preview = preview;
        _commits.Clear();
        _commits.AddRange(preview.Commits);
        string localName = ShortReference(preview.LocalReference)!;
        string remoteName = ShortReference(preview.RemoteReference)!;
        _summaryText = $"{localName} → {preview.RemoteName}/{remoteName}";
        _ = NativeMethods.SetWindowText(_detailTitleLabel, $"{_commits.Count} 个提交");
        _ = NativeMethods.SetWindowText(_targetLabel, $"目标：{preview.RemoteName}/{remoteName}");
        _toolTip?.Update(_targetLabel, $"目标：{preview.RemoteName}/{remoteName}");
        _ = NativeMethods.SetWindowText(_credentialsLabel, UiText.PushCredentialsNotice);
        _ = NativeMethods.ShowWindow(_emptyStateLabel, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_defineRemoteButton, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_pushTagsCheck, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_pushTagsCombo, NativeMethods.ShowHide);
        _ = NativeMethods.SendMessage(_commitList, NativeMethods.ListBoxResetContent, 0, 0);
        foreach (GitPushCommitPreview commit in _commits)
        {
            _ = NativeMethods.SendMessage(_commitList, NativeMethods.ListBoxAddString, 0, commit.Subject);
        }

        _ = NativeMethods.EnableWindow(_pushButton, _commits.Count > 0);
        SetNotice(_commits.Count == 0 ? UiText.PushNothingToPush : string.Empty);
        _previewLoadCompleted = true;
        ApplyDialogLayoutMode(false);
        Layout();
        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    private void SetOperationControlsEnabled(bool enabled)
    {
        foreach (nint control in new[] { _commitList, _defineRemoteButton, _pushButton, _headerCloseButton })
        {
            _ = NativeMethods.EnableWindow(control, enabled);
        }
    }

    private void Layout()
    {
        if (!NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = client.Right;
        int height = client.Bottom;
        int sidebar = _sidebarWidth;
        int bodyTop = _headerHeight;
        int footerTop = height - _footerHeight;
        int contentLeft = S(17);
        int contentTop = bodyTop + _bodyInset;
        int contentRight = width - S(17);
        int contentBottom = ContentBottom(height, contentTop);
        int divider = contentLeft + sidebar;
        Move(
            _defineRemoteButton,
            contentLeft + _defineRemoteLeft,
            contentTop + S(8),
            _defineRemoteWidth,
            _buttonHeight);
        int listTop = contentTop + S(8) + _rowHeight;
        Move(_commitList, contentLeft + S(8), listTop, Math.Max(S(120), sidebar - S(16)), Math.Max(_rowHeight * 2, contentBottom - listTop - S(8)));
        Move(_detailPanel, divider + S(1), contentTop, Math.Max(0, contentRight - divider - S(1)), Math.Max(0, contentBottom - contentTop));
        LayoutDetails();
        Move(_pushTagsCheck, contentLeft, contentBottom + S(5), _pushTagsWidth, _buttonHeight);
        Move(_pushTagsCombo, contentLeft + _pushTagsWidth + S(4), contentBottom + S(7), _pushTagsComboWidth, _buttonHeight);
        int pushLeft = width - S(17) - _pushButtonWidth;
        int cancelLeft = pushLeft - S(8) - _cancelButtonWidth;
        Move(_cancelButton, cancelLeft, height - _footerHeight + S(12), _cancelButtonWidth, _buttonHeight);
        Move(_pushButton, pushLeft, height - _footerHeight + S(12), _pushButtonWidth, _buttonHeight);
        Move(_headerCloseButton, Math.Max(S(32), width - S(45)), Math.Max(S(7), (_headerHeight - _buttonHeight) / 2), S(32), _buttonHeight);
    }

    private void ApplyDialogLayoutMode(bool noRemote, bool force = false)
    {
        if (_noRemoteLayout == noRemote && !force)
        {
            return;
        }

        _noRemoteLayout = noRemote;
        if (_handle == 0)
        {
            return;
        }

        int height = noRemote ? _noRemoteDialogHeight : _dialogHeight;
        if (NativeMethods.GetWindowRectangle(_owner, out var owner))
        {
            _dialogWidth = Math.Min(S(DialogWidth), Math.Max(1, owner.Right - owner.Left - S(80)));
            height = Math.Min(height, Math.Max(1, owner.Bottom - owner.Top - S(88)));
        }
        (int x, int y) = Center(_owner, _dialogWidth, height);
        _ = NativeMethods.MoveWindow(_handle, x, y, _dialogWidth, height, true);
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
        foreach (nint control in new[] { _detailTitleLabel, _targetLabel, _credentialsLabel, _emptyStateLabel, _pushTagsCheck, _pushTagsCombo, _commitList, _noticeLabel, _defineRemoteButton, _pushButton, _cancelButton, _headerCloseButton })
        {
            NativeTheme.ApplyToControl(control, _dark);
        }

        _ = NativeMethods.SendMessage(_detailTitleLabel, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiMediumFont), 1);

        _toolTip?.ApplyAppearance(_dark);
    }

    private nint ApplyControlColor(nint deviceContext, nint control)
    {
        if (deviceContext == 0 || _controlBrush == 0)
        {
            return 0;
        }

        NativeThemePalette palette = NativeTheme.Palette(_dark);
        _ = NativeMethods.SetBackgroundColor(deviceContext, palette.Panel);
        bool muted = control == _targetLabel
            || control == _credentialsLabel
            || control == _noticeLabel
            || control == _emptyStateLabel;
        _ = NativeMethods.SetTextColor(deviceContext, control == _noticeLabel && _noticeError ? palette.Danger : muted ? palette.Muted : palette.Text);
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
            CommitListIdentifier => DrawCommitRow(item),
            21 => NativeComboBoxTheme.DrawItem(parameter, PushTagLabels, _dark),
            CommandPush => NativeTheme.DrawFlatButton(parameter, _dark, emphasized: true),
            CommandDefineRemote => DrawDefineRemoteButton(item),
            CommandCancel => NativeTheme.DrawFlatButton(parameter, _dark, outlined: true),
            CommandClose => NativeTheme.DrawFlatButton(parameter, _dark),
            _ => false,
        };
    }

    private bool DrawDefineRemoteButton(NativeMethods.DrawItem item)
    {
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        bool pressed = (item.ItemState & NativeMethods.OwnerDrawSelected) != 0;
        bool hot = (item.ItemState & NativeMethods.OwnerDrawHotLight) != 0;
        Fill(item.DeviceContext, item.ItemRectangle, hot || pressed ? palette.Hover : palette.AccentSoft);
        NativeMethods.Rectangle textRectangle = item.ItemRectangle;
        _ = NativeMethods.SetTextColor(item.DeviceContext, palette.Accent);
        _ = NativeMethods.SetBackgroundMode(item.DeviceContext, NativeMethods.BackgroundModeTransparent);
        nint previous = NativeMethods.SelectObject(item.DeviceContext, NativeTheme.UiFont);
        string text = NativeMethods.GetWindowTextValue(item.Control);
        _ = NativeMethods.DrawText(
            item.DeviceContext,
            text,
            text.Length,
            ref textRectangle,
            NativeMethods.DrawTextCenter
                | NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextNoPrefix);
        if (previous != 0)
        {
            _ = NativeMethods.SelectObject(item.DeviceContext, previous);
        }

        return true;
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
            Fill(deviceContext, client, palette.Panel);
            Fill(deviceContext, new() { Left = 0, Top = _headerHeight, Right = client.Right, Bottom = _headerHeight + S(1) }, palette.Border);
            Fill(deviceContext, new() { Left = 0, Top = client.Bottom - _footerHeight, Right = client.Right, Bottom = client.Bottom - _footerHeight + S(1) }, palette.Border);
            DrawText(deviceContext, UiText.PushDialogTitle, new() { Left = S(26), Top = S(8), Right = client.Right - S(60), Bottom = _headerHeight - S(7) }, palette.Text, NativeTheme.UiMediumFont);
            int contentLeft = S(17);
            int contentTop = _headerHeight + _bodyInset;
            int contentBottom = ContentBottom(client.Bottom, contentTop);
            int divider = contentLeft + _sidebarWidth;
            Fill(deviceContext, new() { Left = divider, Top = contentTop, Right = divider + S(1), Bottom = contentBottom }, palette.Border);
            NativeMethods.Rectangle selectedTarget = new()
            {
                Left = contentLeft + S(8),
                Top = contentTop + S(8),
                Right = divider - S(8),
                Bottom = contentTop + S(8) + _rowHeight,
            };
            _ = NativeGdiPlusDrawing.FillRoundedRectangle(deviceContext, selectedTarget, palette.AccentSoft, S(10));
            NativeMethods.Rectangle summary = selectedTarget;
            summary.Left = contentLeft + S(SummaryTextOffset);
            summary.Right = NativeMethods.IsWindowVisible(_defineRemoteButton)
                ? contentLeft + _defineRemoteLeft - S(4)
                : summary.Right - S(8);
            DrawText(deviceContext, _summaryText, summary, palette.Text, NativeTheme.UiMediumFont);
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

        return point.Y < _headerHeight && point.X < _dialogWidth - S(54) ? NativeMethods.HitTestCaption : NativeMethods.HitTestClient;
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

    private void DefineRemote()
    {
        if (_closed || _operationRunning || !_previewLoadCompleted || !NativeMethods.IsWindowVisible(_defineRemoteButton))
        {
            return;
        }

        NativeRemoteDialog.Show(_handle, _repository, _service, _settings, _setStatus);
        StartPreview();
    }

    private void UpdateNoRemoteLayoutMetrics()
    {
        int preferredLeft = S(SummaryTextOffset)
            + MeasureTextWidth(_summaryText, NativeTheme.UiMediumFont)
            + S(8);
        _sidebarWidth = Math.Clamp(preferredLeft + _defineRemoteWidth + S(8),
            S(SidebarWidth), Math.Max(S(SidebarWidth), (_dialogWidth - S(34)) / 2));
        _defineRemoteLeft = Math.Min(preferredLeft, _sidebarWidth - _defineRemoteWidth - S(8));
    }

    private static string? ShortReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        const string HeadsPrefix = "refs/heads/";
        const string TagsPrefix = "refs/tags/";
        return reference.StartsWith(HeadsPrefix, StringComparison.Ordinal)
            ? reference[HeadsPrefix.Length..]
            : reference.StartsWith(TagsPrefix, StringComparison.Ordinal)
                ? reference[TagsPrefix.Length..]
                : reference;
    }

    private void Close(bool force = false)
    {
        if (_closed)
        {
            return;
        }

        if (_operationRunning && !force)
        {
            RequestCancellation();
            return;
        }

        nint handle;
        lock (_workGate)
        {
            _closed = true;
            _operationCancellation?.Cancel();
            _previewCancellation?.Cancel();
            // 尚在服务内的任务自行收尾；已投递但未接纳的结果在此释放。
            if (_pendingPush is not null) _operationCancellation?.Dispose();
            if (_pendingPreview is not null) _previewCancellation?.Dispose();
            _operationCancellation = null;
            _previewCancellation = null;
            _pendingPush = null;
            _pendingPreview = null;
            handle = _handle;
            _handle = 0;
        }
        NativeMethods.WakeWindowMessageLoop(handle);
        ClearCommitHover();
        if (_commitList != 0) _ = NativeMethods.RemoveWindowSubclass(_commitList, _commitProcedure, 1);
        NativeComboBoxTheme.Unregister(_pushTagsCombo);
        if (_detailPanel != 0) _ = NativeMethods.RemoveWindowSubclass(_detailPanel, _detailProcedure, 1);
        lock (InstancesGate)
        {
            Instances.Remove(handle);
        }

        _toolTip?.Dispose();
        _toolTip = null;
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }

        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
    }

    public void Dispose()
    {
        Close(force: true);
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
        _ = NativeMethods.DrawText(deviceContext, text, text.Length, ref rectangle, NativeMethods.DrawTextVerticalCenter | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextEndEllipsis);
        if (previous != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previous);
        }
    }

    private int MeasureTextWidth(string text, nint font)
    {
        nint deviceContext = NativeMethods.GetDeviceContext(_handle);
        if (deviceContext == 0)
        {
            return 0;
        }

        nint previous = NativeMethods.SelectObject(deviceContext, font);
        try
        {
            NativeMethods.Rectangle bounds = new();
            _ = NativeMethods.DrawText(
                deviceContext,
                text,
                text.Length,
                ref bounds,
                NativeMethods.DrawTextCalculateRectangle
                    | NativeMethods.DrawTextSingleLine
                    | NativeMethods.DrawTextNoPrefix);
            return bounds.Right - bounds.Left;
        }
        finally
        {
            _ = NativeMethods.SelectObject(deviceContext, previous);
            _ = NativeMethods.ReleaseDeviceContext(_handle, deviceContext);
        }
    }

    private static void DrawHelpIcon(nint deviceContext, int footerTop, uint color)
    {
        NativeMethods.Rectangle circle = new()
        {
            Left = S(15),
            Top = footerTop + S(17),
            Right = S(29),
            Bottom = footerTop + S(31),
        };
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, Math.Max(1, S(1)), color);
        nint previousPen = pen == 0 ? 0 : NativeMethods.SelectObject(deviceContext, pen);
        nint previousBrush = NativeMethods.SelectObject(
            deviceContext,
            NativeMethods.GetStockObject(NativeMethods.NullBrush));
        _ = NativeMethods.DrawEllipse(deviceContext, circle.Left, circle.Top, circle.Right, circle.Bottom);
        if (previousBrush != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousBrush);
        }

        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
        }

        if (pen != 0)
        {
            _ = NativeMethods.DeleteObject(pen);
        }

        DrawText(deviceContext, "?", circle, color, NativeTheme.UiSmallFont);
    }

    private static int S(int pixels) => NativeTheme.Scale(pixels);
}
