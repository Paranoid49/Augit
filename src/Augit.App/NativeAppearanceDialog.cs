using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed class NativeAppearanceDialog : IDisposable
{
    private const string WindowClassName = "Augit.AppearanceDialog.Native";
    private const int DialogWidth = 1040;
    // 视觉稿的设置窗口由 45 像素标题栏、650 像素内容区和 53 像素操作栏组成。
    // 内容区还包含上下各 15 像素的内边距，因此推荐窗口总高为 779 像素。
    private const int DialogHeight = 779;
    private const int HeaderHeight = 45;
    private const int FooterHeight = 53;
    private const int SidebarWidth = 262;
    private const int SearchIdentifier = 100;
    private const int CategoryIdentifierBase = 200;
    private const int ContentTitleIdentifier = 400;
    private const int CommandConfirm = 501;
    private const int CommandApply = 502;
    private const int CommandCancel = 503;
    private const int CommandClose = 504;
    private const int ThemeIdentifier = 510;
    private const int TextFontIdentifier = 511;
    private const int MonospaceFontIdentifier = 512;
    private const int FontSizeIdentifier = 513;
    private const int GitExecutableIdentifier = 514;
    private const int TerminalShellIdentifier = 515;
    private const int TerminalCustomIdentifier = 516;
    private const int TextFontSizeIdentifier = 517;
    private static readonly string[] ThemeLabels =
    [
        UiText.FollowWindows,
        UiText.Light,
        UiText.Dark,
    ];
    private static readonly string[] TerminalShellLabels =
    [
        UiText.WindowsPowerShell,
        UiText.PowerShell7,
        UiText.CommandPrompt,
        UiText.GitBash,
        UiText.Wsl,
        UiText.CustomTerminal,
    ];
    private static readonly string[] CategoryLabels =
    [
        UiText.AppearanceAndBehavior,
        UiText.Appearance,
        UiText.FileViewing,
        UiText.GitSection,
        UiText.TerminalSection,
    ];
    private static readonly string[] AppearanceSectionLabels =
    [
        UiText.ThemeSection,
        UiText.InterfaceFont,
        UiText.WindowSection,
    ];
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeAppearanceDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly ApplicationSettings _initialSettings;
    private readonly Action<ApplicationSettings>? _previewSettings;
    private readonly List<nint> _controls = [];
    private readonly List<nint> _categoryButtons = [];
    private ApplicationSettings _previewBaseSettings;
    private nint _handle;
    private nint _searchEdit;
    private nint _contentTitle;
    private nint _themeLabel;
    private nint _themeSectionLabel;
    private nint _themeCombo;
    private nint _appearanceHint;
    private nint _textFontLabel;
    private nint _fontSectionLabel;
    private nint _textFontEdit;
    private nint _textFontSizeLabel;
    private nint _textFontSizeEdit;
    private nint _monospaceFontLabel;
    private nint _monospaceFontEdit;
    private nint _fontSizeLabel;
    private nint _fontSizeEdit;
    private nint _fontHint;
    private nint _windowHint;
    private nint _windowSectionLabel;
    private nint _gitLabel;
    private nint _gitExecutableEdit;
    private nint _gitHint;
    private nint _terminalShellLabel;
    private nint _terminalShellCombo;
    private nint _terminalCustomLabel;
    private nint _terminalCustomEdit;
    private nint _terminalHint;
    private nint _controlBrush;
    private NativeToolTip? _toolTip;
    private nint _headingFont;
    private nint _closeButton;
    private nint _cancelButton;
    private nint _applyButton;
    private nint _confirmButton;
    private bool _closed;
    private bool _dark;
    private int _selectedCategory;
    private int _dialogWidth;
    private int _dialogHeight;
    private int _headerLogical = HeaderHeight;
    private int _footerLogical = FooterHeight;
    private int _rowLogical = 30;
    private int _buttonLogical = 30;
    private int _sidebarLogical = SidebarWidth;
    private int[] _appearanceSectionLines = [124, 222, 379];
    private int _contentScrollOffset;
    private int _contentScrollMaximum;
    private NativeMethods.Rectangle _searchEditFrame;
    private NativeMethods.Rectangle _themeComboFrame;
    private NativeMethods.Rectangle _textFontEditFrame;
    private NativeMethods.Rectangle _textFontSizeEditFrame;
    private NativeMethods.Rectangle _monospaceFontEditFrame;
    private NativeMethods.Rectangle _fontSizeEditFrame;
    private NativeMethods.Rectangle _gitExecutableEditFrame;
    private NativeMethods.Rectangle _terminalShellComboFrame;
    private NativeMethods.Rectangle _terminalCustomEditFrame;
    private ApplicationSettings? _result;

    private NativeAppearanceDialog(
        nint owner,
        ApplicationSettings settings,
        Action<ApplicationSettings>? previewSettings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _owner = owner;
        _initialSettings = settings;
        _previewBaseSettings = settings;
        _previewSettings = previewSettings;
        _dark = NativeTheme.IsDark(settings.Theme);
        EnsureWindowClass();
        ApplyAdaptiveMetrics();
        int width = S(DialogWidth);
        int height = S(DialogHeight);
        int x = NativeMethods.UseDefault;
        int y = NativeMethods.UseDefault;
        if (NativeMethods.GetWindowRectangle(owner, out NativeMethods.Rectangle ownerRectangle))
        {
            // 模态窗口遵守视觉稿的边距约束，避免最小主窗口下被宿主边界裁切。
            (int ownerWidth, int ownerHeight) = GetResponsiveLogicalSizeForTest(
                (int)Math.Round(NativeTheme.Unscale(ownerRectangle.Right - ownerRectangle.Left)),
                (int)Math.Round(NativeTheme.Unscale(ownerRectangle.Bottom - ownerRectangle.Top)));
            width = S(ownerWidth);
            height = S(ownerHeight);
            x = ownerRectangle.Left + Math.Max(0, ((ownerRectangle.Right - ownerRectangle.Left) - width) / 2);
            y = ownerRectangle.Top + Math.Max(0, ((ownerRectangle.Bottom - ownerRectangle.Top) - height) / 2);
        }

        _dialogWidth = width;
        _dialogHeight = height;

        uint windowStyle = NativeMethods.WindowStylePopup | NativeMethods.WindowStyleClipChildren;
        if (NativeTheme.UiFontSizeForTest > NativeTheme.UiFontLogicalSizeForTest)
        {
            windowStyle |= NativeMethods.WindowStyleVerticalScroll;
        }

        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.SettingsTitle,
            windowStyle,
            x,
            y,
            width,
            height,
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.AppearanceWindowCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        _headingFont = NativeTheme.CreateOwnedUiFont(_initialSettings.TextFontFamily, _initialSettings.UiFontSize, 600);
        CreateControls();
        _toolTip = new NativeToolTip(_handle);
        _toolTip.Add(_closeButton, UiText.Close);
        ApplyAppearance(_dark);
        LayoutControls();
        SelectCategory(0);
    }

    internal static ApplicationSettings? Show(
        nint owner,
        ApplicationSettings settings,
        Action<ApplicationSettings>? previewSettings = null)
    {
        NativeAppearanceDialog dialog = new(owner, settings, previewSettings);
        return dialog.Run();
    }

    internal static (int Width, int Height, int SidebarWidth, int HeaderHeight, int FooterHeight) LogicalLayoutForTest =>
        (DialogWidth, DialogHeight, SidebarWidth, HeaderHeight, FooterHeight);

    internal static uint ComboControlStyleForTest =>
        NativeMethods.ComboBoxDropDownList
        | NativeMethods.ComboBoxOwnerDrawFixed
        | NativeMethods.ComboBoxHasStrings;

    internal static uint InputControlStyleForTest => NativeMethods.EditAutoHorizontalScroll;

    internal static IReadOnlyList<string> ThemeLabelsForTest => ThemeLabels;

    internal static IReadOnlyList<string> AppearanceSectionLabelsForTest => AppearanceSectionLabels;

    internal static IReadOnlyList<int> AppearanceSectionTopForTest => [123, 245, 453];

    internal static IReadOnlyList<string> TerminalShellLabelsForTest => TerminalShellLabels;

    internal static (int Width, int Height) GetResponsiveLogicalSizeForTest(int ownerWidth, int ownerHeight)
    {
        int safeOwnerWidth = Math.Max(0, ownerWidth);
        int safeOwnerHeight = Math.Max(0, ownerHeight);
        return (
            Math.Min(DialogWidth, Math.Max(760, safeOwnerWidth - 80)),
            Math.Min(DialogHeight, Math.Max(480, safeOwnerHeight - 106)));
    }

    internal static (int Header, int Footer, int Row, int Button, int Height) CalculateAdaptiveMetricsForTest(int lineHeight)
    {
        int line = Math.Max(13, lineHeight);
        int header = Math.Max(HeaderHeight, line + 18);
        int footer = Math.Max(FooterHeight, line + 25);
        int row = Math.Max(30, line + 8);
        int button = Math.Max(30, line + 4);
        int height = Math.Max(DialogHeight, header + footer + (row * 14) + 220);
        return (header, footer, row, button, height);
    }

    internal static bool IsContentFullyVisibleForTest(int top, int height, int viewportTop, int viewportBottom)
    {
        return height > 0 && top >= viewportTop && top + height <= viewportBottom;
    }

    internal static int CalculateContentScrollMaximumForTest(int viewportHeight, int contentHeight)
    {
        return Math.Max(0, contentHeight - Math.Max(0, viewportHeight));
    }

    private static int GetLogicalLineHeightForTest()
    {
        int physical = NativeTheme.UiLineHeight;
        return Math.Max(13, (int)Math.Ceiling(NativeTheme.Unscale(physical)));
    }

    private void ApplyAdaptiveMetrics()
    {
        (int header, int footer, int row, int button, _) = CalculateAdaptiveMetricsForTest(GetLogicalLineHeightForTest());
        _headerLogical = header;
        _footerLogical = footer;
        _rowLogical = row;
        _buttonLogical = button;
        _sidebarLogical = SidebarWidth;
        _appearanceSectionLines = [124, 222, 379];
        if (NativeTheme.UiFontSizeForTest <= NativeTheme.UiFontLogicalSizeForTest)
        {
            return;
        }

        int line = GetLogicalLineHeightForTest();
        int gap = Math.Max(8, line / 3);
        _appearanceSectionLines = [
            _headerLogical + 29,
            _headerLogical + 29 + _rowLogical + gap + 20,
            _headerLogical + 29 + (_rowLogical * 4) + (gap * 4) + 52,
        ];
    }

    private ApplicationSettings? Run()
    {
        using NativeModalFocusScope focusScope = new(_owner);
        using NativeModalScrim scrim = NativeModalScrim.Begin(_owner, _dark);
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        _ = NativeMethods.SetFocus(_searchEdit);
        try
        {
            while (!_closed && NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0) > 0)
            {
                if (message.MessageId == NativeMethods.WindowMessageKeyDown
                    && unchecked((int)message.WordParameter) == NativeMethods.VirtualKeyTab)
                {
                    MoveFocus(NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0);
                    continue;
                }

                if (message.MessageId == NativeMethods.WindowMessageKeyDown
                    && unchecked((int)message.WordParameter) == NativeMethods.VirtualKeyEscape)
                {
                    Close();
                    continue;
                }

                if (NativeMethods.IsDialogMessage(_handle, ref message))
                {
                    continue;
                }

                _ = NativeMethods.TranslateMessage(ref message);
                _ = NativeMethods.DispatchMessage(ref message);
            }
        }
        finally
        {
            _ = NativeMethods.EnableWindow(_owner, true);
            _ = NativeMethods.SetForegroundWindow(_owner);
            focusScope.Restore();
            Close();
        }

        return _result;
    }

    /// <summary>
    /// 按设置窗口的视觉顺序循环移动焦点，隐藏的分类内容会被跳过。
    /// </summary>
    private void MoveFocus(bool backwards)
    {
        NativeFocusNavigation.MoveWithinRegion(
            [
                _searchEdit,
                .. _categoryButtons,
                _themeCombo,
                _textFontEdit,
                _textFontSizeEdit,
                _monospaceFontEdit,
                _fontSizeEdit,
                _gitExecutableEdit,
                _terminalShellCombo,
                _terminalCustomEdit,
                _cancelButton,
                _applyButton,
                _confirmButton,
                _closeButton,
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
                Background = NativeMethods.GetSystemColorBrush(NativeMethods.ColorWindow),
                ClassName = WindowClassName,
            };
            ushort atom = NativeMethods.RegisterClass(ref windowClass);
            int error = Marshal.GetLastWin32Error();
            if (atom == 0 && error != NativeMethods.ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error, UiText.AppearanceWindowClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeAppearanceDialog? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        switch (message)
        {
            case NativeMethods.WindowMessageSize:
                instance.LayoutControls();
                return 0;
            case NativeMethods.WindowMessageVerticalScroll:
                instance.HandleContentScroll(wordParameter);
                return 0;
            case NativeMethods.WindowMessageMouseWheel:
                instance.HandleContentWheel(wordParameter);
                return 0;
            case NativeMethods.WindowMessagePaint:
                instance.PaintWindow();
                return 0;
            case NativeMethods.WindowMessageEraseBackground:
                return 1;
            case NativeMethods.WindowMessageNonClientHitTest:
                return instance.HitTest();
            case NativeMethods.WindowMessageDrawItem:
                return instance.DrawControl(longParameter) ? 1 : 0;
            case NativeMethods.WindowMessageCommand:
                instance.HandleCommand(wordParameter);
                return 0;
            case NativeMethods.WindowMessageControlColorEdit:
            case NativeMethods.WindowMessageControlColorListBox:
            case NativeMethods.WindowMessageControlColorButton:
            case NativeMethods.WindowMessageControlColorStatic:
                return instance.ApplyControlColor(unchecked((nint)wordParameter));
            case NativeMethods.WindowMessageClose:
                instance.Close();
                return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private void CreateControls()
    {
        _searchEdit = CreateChild(
            NativeMethods.EditClass,
            string.Empty,
            SearchIdentifier,
            NativeMethods.EditAutoHorizontalScroll,
            26,
            70,
            224,
            30);
        _ = NativeMethods.SendMessage(_searchEdit, NativeMethods.EditSetCueBanner, 1, UiText.SearchSettings);

        for (int index = 0; index < CategoryLabels.Length; index++)
        {
            _categoryButtons.Add(CreateChild(
                NativeMethods.ButtonClass,
                CategoryLabels[index],
                CategoryIdentifierBase + index,
                NativeMethods.ButtonOwnerDraw,
                26,
                112 + index * 38,
                224,
                32));
        }

        _contentTitle = CreateChild(
            NativeMethods.StaticClass,
            string.Empty,
            ContentTitleIdentifier,
            NativeMethods.StaticLeft,
            292,
            74,
            700,
            34);
        _ = NativeMethods.SendMessage(
            _contentTitle,
            NativeMethods.WindowMessageSetFont,
            unchecked((nuint)_headingFont),
            1);

        _themeSectionLabel = CreateLabel(UiText.ThemeSection, 292, 112, 110, 24);
        _ = NativeMethods.SendMessage(
            _themeSectionLabel,
            NativeMethods.WindowMessageSetFont,
            unchecked((nuint)NativeTheme.UiMediumFont),
            1);
        _themeLabel = CreateLabel(UiText.Theme, 292, 145);
        _themeCombo = CreateChild(
            NativeMethods.ComboBoxClass,
            string.Empty,
            ThemeIdentifier,
            ComboControlStyleForTest,
            410,
            137,
            570,
            150);
        foreach (string theme in ThemeLabels)
        {
            _ = NativeMethods.SendMessage(_themeCombo, NativeMethods.ComboBoxAddString, 0, theme);
        }

        SetComboItemHeight(_themeCombo, 30);
        if (!NativeComboBoxTheme.Register(_themeCombo, () => _dark, 30, parentDrawsFrame: true))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.AppearanceControlCreateFailed);
        }

        int themeIndex = ThemeIndexForTest(_initialSettings.Theme);
        _ = NativeMethods.SendMessage(_themeCombo, NativeMethods.ComboBoxSetCurrentSelection, unchecked((nuint)themeIndex), 0);
        _appearanceHint = CreateLabel(UiText.AppearanceDescription, 410, 174, 570, 30, centerImage: false);
        _fontSectionLabel = CreateLabel(UiText.InterfaceFont, 292, 210, 110, 24);
        _ = NativeMethods.SendMessage(
            _fontSectionLabel,
            NativeMethods.WindowMessageSetFont,
            unchecked((nuint)NativeTheme.UiMediumFont),
            1);
        _textFontLabel = CreateLabel(UiText.InterfaceFont, 292, 244);
        _textFontEdit = CreateEdit(TextFontIdentifier, _initialSettings.TextFontFamily, 410, 236, 570);
        _textFontSizeLabel = CreateLabel(UiText.FontSize, 292, 278);
        _textFontSizeEdit = CreateEdit(
            TextFontSizeIdentifier,
            _initialSettings.UiFontSize.ToString(CultureInfo.InvariantCulture),
            410,
            270,
            80);
        _monospaceFontLabel = CreateLabel(UiText.MonospaceFont, 292, 278);
        _monospaceFontEdit = CreateEdit(MonospaceFontIdentifier, _initialSettings.MonospaceFontFamily, 410, 270, 570);
        _fontSizeLabel = CreateLabel(UiText.FontSize, 292, 312);
        _fontSizeEdit = CreateEdit(
            FontSizeIdentifier,
            _initialSettings.FontSize.ToString(CultureInfo.InvariantCulture),
            410,
            304,
            150);
        _fontHint = CreateLabel(UiText.DisplayOnlyDescription, 410, 344, 570, 24, centerImage: false);
        _windowSectionLabel = CreateLabel(UiText.WindowSection, 292, 367, 110, 24);
        _ = NativeMethods.SendMessage(
            _windowSectionLabel,
            NativeMethods.WindowMessageSetFont,
            unchecked((nuint)NativeTheme.UiMediumFont),
            1);
        _windowHint = CreateLabel(UiText.WorkspaceRestoreDescription, 410, 394, 570, 24, centerImage: false);

        _gitLabel = CreateLabel(UiText.GitExecutablePath, 292, 130);
        _gitExecutableEdit = CreateEdit(
            GitExecutableIdentifier,
            _initialSettings.GitExecutablePath ?? string.Empty,
            410,
            122,
            570);
        _gitHint = CreateLabel(UiText.GitExecutableDescription, 410, 164, 570, 42);

        _terminalShellLabel = CreateLabel(UiText.TerminalProfile, 292, 130);
        _terminalShellCombo = CreateChild(
            NativeMethods.ComboBoxClass,
            string.Empty,
            TerminalShellIdentifier,
            ComboControlStyleForTest,
            410,
            122,
            570,
            220);
        foreach (string shell in TerminalShellLabels)
        {
            _ = NativeMethods.SendMessage(_terminalShellCombo, NativeMethods.ComboBoxAddString, 0, shell);
        }

        SetComboItemHeight(_terminalShellCombo, 30);
        if (!NativeComboBoxTheme.Register(_terminalShellCombo, () => _dark, 30, parentDrawsFrame: true))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.AppearanceControlCreateFailed);
        }

        int shellIndex = ShellIndexForTest(_initialSettings.TerminalShell);
        _ = NativeMethods.SendMessage(_terminalShellCombo, NativeMethods.ComboBoxSetCurrentSelection, unchecked((nuint)shellIndex), 0);
        _terminalCustomLabel = CreateLabel(UiText.TerminalCustomCommand, 292, 176);
        _terminalCustomEdit = CreateEdit(
            TerminalCustomIdentifier,
            _initialSettings.TerminalCustomCommand ?? string.Empty,
            410,
            168,
            570);
        _terminalHint = CreateLabel(UiText.TerminalDescription, 410, 210, 570, 42);

        _closeButton = CreateChild(
            NativeMethods.ButtonClass,
            UiText.CloseSymbol,
            CommandClose,
            NativeMethods.ButtonOwnerDraw,
            990,
            8,
            34,
            32);
        _cancelButton = CreateChild(
            NativeMethods.ButtonClass,
            UiText.Cancel,
            CommandCancel,
            NativeMethods.ButtonOwnerDraw,
            792,
            568,
            72,
            32);
        _applyButton = CreateChild(
            NativeMethods.ButtonClass,
            UiText.Apply,
            CommandApply,
            NativeMethods.ButtonOwnerDraw,
            872,
            568,
            72,
            32);
        _confirmButton = CreateChild(
            NativeMethods.ButtonClass,
            UiText.Confirm,
            CommandConfirm,
            NativeMethods.ButtonOwnerDraw,
            952,
            568,
            72,
            32);
        HideAllContent();
    }

    private void LayoutControls()
    {
        if (_handle == 0 || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = Math.Max(S(760), client.Right - client.Left);
        int height = Math.Max(S(480), client.Bottom - client.Top);
        _dialogWidth = width;
        _dialogHeight = height;
        int logicalWidth = Math.Max(760, (int)Math.Round(NativeTheme.Unscale(width)));
        int logicalHeight = Math.Max(480, (int)Math.Round(NativeTheme.Unscale(height)));
        if (NativeTheme.UiFontSizeForTest > NativeTheme.UiFontLogicalSizeForTest)
        {
            LayoutLargeTypography(logicalWidth, logicalHeight);
            return;
        }
        // 对应视觉稿 settings-page 的 28 像素内容内边距。
        int contentLeft = 290;
        int rightInset = 24;
        int fieldWidth = Math.Max(260, logicalWidth - 410 - rightInset);
        int closeX = Math.Max(700, logicalWidth - 41);
        int confirmX = Math.Max(760, logicalWidth - 65);
        int applyX = Math.Max(680, confirmX - 61);
        int cancelX = Math.Max(600, applyX - 61);

        MoveInputControl(_searchEdit, ref _searchEditFrame, 26, 70, 224, 30);
        for (int index = 0; index < _categoryButtons.Count; index++)
        {
            MoveControl(_categoryButtons[index], 26, 112 + index * 38, 224, 32);
        }

        MoveControl(_contentTitle, contentLeft, 82, Math.Max(300, logicalWidth - contentLeft - rightInset), 34);
        MoveControl(_themeSectionLabel, contentLeft, 123, 110, 24);
        MoveControl(_themeLabel, contentLeft, 153, 105, 24);
        MoveInputControl(_themeCombo, ref _themeComboFrame, 410, 160, fieldWidth, 30, isCombo: true);
        MoveControl(_appearanceHint, 410, 202, fieldWidth, 24);
        MoveControl(_fontSectionLabel, contentLeft, 245, 110, 24);
        MoveControl(_textFontLabel, contentLeft, 281, 105, 30);
        int sizeLeft = logicalWidth - rightInset - 70;
        int sizeLabelLeft = sizeLeft - 42;
        int fontFieldWidth = sizeLabelLeft - 410 - 12;
        MoveInputControl(_textFontEdit, ref _textFontEditFrame, 410, 281, fontFieldWidth, 30);
        MoveControl(_textFontSizeLabel, sizeLabelLeft, 281, 36, 30);
        MoveInputControl(_textFontSizeEdit, ref _textFontSizeEditFrame, sizeLeft, 281, 70, 30);
        MoveControl(_monospaceFontLabel, contentLeft, 324, 105, 30);
        MoveInputControl(_monospaceFontEdit, ref _monospaceFontEditFrame, 410, 324, fontFieldWidth, 30);
        MoveControl(_fontSizeLabel, sizeLabelLeft, 324, 36, 30);
        MoveInputControl(_fontSizeEdit, ref _fontSizeEditFrame, sizeLeft, 324, 70, 30);
        MoveControl(_fontHint, 410, 410, fieldWidth, 24);
        MoveControl(_windowSectionLabel, contentLeft, 453, 110, 24);
        MoveControl(_windowHint, 410, Math.Min(490, logicalHeight - FooterHeight - 32), fieldWidth, 24);
        MoveControl(_gitLabel, contentLeft, 141, 105, 24);
        MoveInputControl(_gitExecutableEdit, ref _gitExecutableEditFrame, 410, 137, fieldWidth, 30);
        MoveControl(_gitHint, 410, 179, fieldWidth, 42);
        MoveControl(_terminalShellLabel, contentLeft, 141, 105, 24);
        MoveInputControl(_terminalShellCombo, ref _terminalShellComboFrame, 410, 137, fieldWidth, 30, isCombo: true);
        MoveControl(_terminalCustomLabel, contentLeft, 187, 105, 24);
        MoveInputControl(_terminalCustomEdit, ref _terminalCustomEditFrame, 410, 183, fieldWidth, 30);
        MoveControl(_terminalHint, 410, 225, fieldWidth, 42);
        MoveControl(_closeButton, closeX, 9, 27, 27);
        int actionTop = logicalHeight - 42;
        MoveControl(_cancelButton, cancelX, actionTop, 53, 30);
        MoveControl(_applyButton, applyX, actionTop, 53, 30);
        MoveControl(_confirmButton, confirmX, actionTop, 52, 30);
    }

    private void HandleContentScroll(nuint parameter)
    {
        if (NativeTheme.UiFontSizeForTest <= NativeTheme.UiFontLogicalSizeForTest)
        {
            return;
        }

        int command = NativeMethods.LowWord(parameter);
        int next = _contentScrollOffset;
        switch (command)
        {
            case 0: next -= _rowLogical; break;
            case 1: next += _rowLogical; break;
            case 2: next -= Math.Max(_rowLogical, _contentScrollOffset); break;
            case 3: next += _rowLogical; break;
            case 6: next = 0; break;
            case 7: next = _contentScrollMaximum; break;
            case 4: next = (int)NativeMethods.HighWord(parameter); break;
        }

        SetContentScrollOffset(next);
    }

    private void HandleContentWheel(nuint parameter)
    {
        if (NativeTheme.UiFontSizeForTest <= NativeTheme.UiFontLogicalSizeForTest)
        {
            return;
        }

        short delta = unchecked((short)((long)parameter >> 16));
        if (delta == 0)
        {
            return;
        }

        int steps = Math.Max(1, Math.Abs(delta) / 120);
        SetContentScrollOffset(_contentScrollOffset - Math.Sign(delta) * _rowLogical * steps);
    }

    private void SetContentScrollOffset(int offset)
    {
        int next = Math.Clamp(offset, 0, _contentScrollMaximum);
        if (next == _contentScrollOffset)
        {
            return;
        }

        _contentScrollOffset = next;
        LayoutControls();
        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    private void UpdateContentScrollRange(int logicalViewportHeight, int logicalContentHeight)
    {
        _contentScrollMaximum = CalculateContentScrollMaximumForTest(logicalViewportHeight, logicalContentHeight);
        _contentScrollOffset = Math.Clamp(_contentScrollOffset, 0, _contentScrollMaximum);
        ScrollInfo info = new()
        {
            Size = (uint)Marshal.SizeOf<ScrollInfo>(),
            Mask = 7,
            Minimum = 0,
            Maximum = Math.Max(0, logicalContentHeight - 1),
            Page = (uint)Math.Max(0, logicalViewportHeight),
            Position = _contentScrollOffset,
        };
        _ = SetScrollInformation(_handle, 1, ref info, true);
    }

    private struct ScrollInfo
    {
        public uint Size;
        public uint Mask;
        public int Minimum;
        public int Maximum;
        public uint Page;
        public int Position;
        public int TrackPosition;
    }

    [DllImport("user32.dll", EntryPoint = "SetScrollInfo")]
    private static extern int SetScrollInformation(nint window, int bar, ref ScrollInfo info, bool redraw);

    private void LayoutLargeTypography(int logicalWidth, int logicalHeight)
    {
        int line = GetLogicalLineHeightForTest();
        int row = _rowLogical;
        int gap = Math.Max(8, line / 3);
        int sectionHeight = Math.Max(24, line + 8);
        int titleHeight = Math.Max(34, line + 10);
        int contentLeft = 290;
        int rightInset = 24;
        int labelWidth = Math.Max(110, MeasureLogicalTextWidth("界面字体", NativeTheme.UiFont) + 14);
        int fieldLeft = contentLeft + labelWidth + 20;
        int sizeEditWidth = Math.Max(70, MeasureLogicalTextWidth("40", NativeTheme.UiFont) + 24);
        int sizeLabelWidth = Math.Max(36, MeasureLogicalTextWidth(UiText.FontSize, NativeTheme.UiFont) + 8);
        int fieldWidth = Math.Max(220, logicalWidth - fieldLeft - sizeLabelWidth - sizeEditWidth - 2 * gap - rightInset);
        int fullFieldWidth = Math.Max(220, logicalWidth - fieldLeft - rightInset);
        int titleTop = _headerLogical + 24;
        void MoveContent(nint control, int x, int y, int width, int height) =>
            MoveContentControl(control, x, y - _contentScrollOffset, width, height, logicalHeight);
        void MoveInputContent(nint control, ref NativeMethods.Rectangle frame, int x, int y, int width, int height, bool isCombo = false) =>
            MoveInputContentControl(control, ref frame, x, y - _contentScrollOffset, width, height, logicalHeight, isCombo);
        MoveControl(_searchEdit, 26, _headerLogical + 22, 224, row);
        int categoryTop = _headerLogical + 22 + row + gap;
        int categoryHeight = Math.Max(32, line + 8);
        int categoryGap = Math.Max(6, line / 3);
        for (int index = 0; index < _categoryButtons.Count; index++)
        {
            MoveControl(_categoryButtons[index], 26, categoryTop + index * (categoryHeight + categoryGap), 224, categoryHeight);
        }

        MoveContent(_contentTitle, contentLeft, titleTop, Math.Max(300, logicalWidth - contentLeft - rightInset), titleHeight);
        int y = titleTop + titleHeight + gap;

        int themeSectionTop = y;
        MoveContent(_themeSectionLabel, contentLeft, y, labelWidth, sectionHeight);
        y += sectionHeight + gap;
        MoveContent(_themeLabel, contentLeft, y, labelWidth, row);
        MoveInputContent(_themeCombo, ref _themeComboFrame, fieldLeft, y, fullFieldWidth, row, isCombo: true);
        y += row + gap;
        int appearanceHintHeight = Math.Max(row, SetWrappedLabel(_appearanceHint, UiText.AppearanceDescription, fullFieldWidth) * line + 4);
        MoveContent(_appearanceHint, fieldLeft, y, fullFieldWidth, appearanceHintHeight);
        y += appearanceHintHeight + gap;
        int fontSectionTop = y;
        MoveContent(_fontSectionLabel, contentLeft, fontSectionTop, labelWidth, sectionHeight);
        y += sectionHeight + gap;
        MoveContent(_textFontLabel, contentLeft, y, labelWidth, row);
        MoveInputContent(_textFontEdit, ref _textFontEditFrame, fieldLeft, y, fieldWidth, row);
        MoveContent(_textFontSizeLabel, fieldLeft + fieldWidth + gap, y, sizeLabelWidth, row);
        MoveInputContent(_textFontSizeEdit, ref _textFontSizeEditFrame, fieldLeft + fieldWidth + gap + sizeLabelWidth, y, sizeEditWidth, row);
        y += row + gap;
        MoveContent(_monospaceFontLabel, contentLeft, y, labelWidth, row);
        MoveInputContent(_monospaceFontEdit, ref _monospaceFontEditFrame, fieldLeft, y, fieldWidth, row);
        MoveContent(_fontSizeLabel, fieldLeft + fieldWidth + gap, y, sizeLabelWidth, row);
        MoveInputContent(_fontSizeEdit, ref _fontSizeEditFrame, fieldLeft + fieldWidth + gap + sizeLabelWidth, y, sizeEditWidth, row);
        y += row + gap;
        int fontHintHeight = Math.Max(row, SetWrappedLabel(_fontHint, UiText.DisplayOnlyDescription, fullFieldWidth) * line + 4);
        int fontHintTop = y;
        MoveContent(_fontHint, fieldLeft, fontHintTop, fullFieldWidth, fontHintHeight);
        y += fontHintHeight + gap;
        int windowSectionTop = y;
        MoveContent(_windowSectionLabel, contentLeft, windowSectionTop, labelWidth, sectionHeight);
        y += sectionHeight + gap;
        int windowHintHeight = Math.Max(row, SetWrappedLabel(_windowHint, UiText.WorkspaceRestoreDescription, fullFieldWidth) * line + 4);
        int windowHintTop = y;
        MoveContent(_windowHint, fieldLeft, windowHintTop, fullFieldWidth, windowHintHeight);
        _appearanceSectionLines = [themeSectionTop, fontSectionTop, windowSectionTop];

        int gitY = titleTop + titleHeight + gap;
        MoveContent(_gitLabel, contentLeft, gitY, labelWidth, row);
        MoveInputContent(_gitExecutableEdit, ref _gitExecutableEditFrame, fieldLeft, gitY, fullFieldWidth, row);
        gitY += row + gap;
        int gitHintHeight = Math.Max(row, SetWrappedLabel(_gitHint, UiText.GitExecutableDescription, fullFieldWidth) * line + 4);
        int gitHintTop = gitY;
        MoveContent(_gitHint, fieldLeft, gitHintTop, fullFieldWidth, gitHintHeight);

        int terminalY = titleTop + titleHeight + gap;
        MoveContent(_terminalShellLabel, contentLeft, terminalY, labelWidth, row);
        MoveInputContent(_terminalShellCombo, ref _terminalShellComboFrame, fieldLeft, terminalY, fullFieldWidth, row, isCombo: true);
        terminalY += row + gap;
        MoveContent(_terminalCustomLabel, contentLeft, terminalY, labelWidth, row);
        MoveInputContent(_terminalCustomEdit, ref _terminalCustomEditFrame, fieldLeft, terminalY, fullFieldWidth, row);
        terminalY += row + gap;
        int terminalHintHeight = Math.Max(row, SetWrappedLabel(_terminalHint, UiText.TerminalDescription, fullFieldWidth) * line + 4);
        int terminalHintTop = terminalY;
        MoveContent(_terminalHint, fieldLeft, terminalHintTop, fullFieldWidth, terminalHintHeight);

        SetComboItemHeight(_themeCombo, row);
        SetComboItemHeight(_terminalShellCombo, row);
        int actionTop = logicalHeight - _footerLogical + Math.Max(0, (_footerLogical - _buttonLogical) / 2);
        int confirmWidth = Math.Max(52, MeasureLogicalTextWidth(UiText.Confirm, NativeTheme.UiFont) + 26);
        int applyWidth = Math.Max(53, MeasureLogicalTextWidth(UiText.Apply, NativeTheme.UiFont) + 26);
        int cancelWidth = Math.Max(53, MeasureLogicalTextWidth(UiText.Cancel, NativeTheme.UiFont) + 26);
        int actionGap = Math.Max(8, line / 3);
        int confirmX = logicalWidth - rightInset - confirmWidth;
        int applyX = confirmX - actionGap - applyWidth;
        int cancelX = applyX - actionGap - cancelWidth;
        MoveControl(_closeButton, Math.Max(contentLeft, logicalWidth - 41), 9, 27, 27);
        MoveControl(_cancelButton, Math.Max(contentLeft, cancelX), actionTop, cancelWidth, _buttonLogical);
        MoveControl(_applyButton, Math.Max(contentLeft, applyX), actionTop, applyWidth, _buttonLogical);
        MoveControl(_confirmButton, Math.Max(contentLeft, confirmX), actionTop, confirmWidth, _buttonLogical);
        int selectedContentBottom = _selectedCategory switch
        {
            0 => windowHintTop + windowHintHeight,
            1 or 2 => fontHintTop + fontHintHeight,
            3 => gitHintTop + gitHintHeight,
            4 => terminalHintTop + terminalHintHeight,
            _ => y,
        };
        UpdateContentScrollRange(
            Math.Max(1, logicalHeight - _headerLogical - _footerLogical),
            Math.Max(logicalHeight - _headerLogical - _footerLogical, selectedContentBottom));
    }

    private int MeasureLogicalTextWidth(string text, nint font)
    {
        nint deviceContext = NativeMethods.GetDeviceContext(_handle);
        if (deviceContext == 0)
        {
            return text.Length * Math.Max(13, GetLogicalLineHeightForTest());
        }

        nint previous = NativeMethods.SelectObject(deviceContext, font);
        try
        {
            NativeMethods.Rectangle bounds = new();
            _ = NativeMethods.DrawText(deviceContext, text, text.Length, ref bounds,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
            return Math.Max(0, (int)Math.Ceiling(NativeTheme.Unscale(bounds.Right - bounds.Left)));
        }
        finally
        {
            _ = NativeMethods.SelectObject(deviceContext, previous);
            _ = NativeMethods.ReleaseDeviceContext(_handle, deviceContext);
        }
    }

    private void MoveContentControl(nint control, int x, int y, int width, int height, int logicalHeight)
    {
        MoveControl(control, x, y, width, height);
        UpdateContentControlVisibility(control, y, height, logicalHeight);
    }

    private void MoveInputContentControl(
        nint control,
        ref NativeMethods.Rectangle frame,
        int x,
        int y,
        int width,
        int height,
        int logicalHeight,
        bool isCombo)
    {
        MoveInputControl(control, ref frame, x, y, width, height, isCombo);
        UpdateContentControlVisibility(control, y, height, logicalHeight);
    }

    private void UpdateContentControlVisibility(nint control, int top, int height, int logicalHeight)
    {
        if (control == 0 || !IsSelectedContentControl(control))
        {
            return;
        }

        bool visible = IsContentFullyVisibleForTest(
            top,
            height,
            _headerLogical,
            Math.Max(_headerLogical, logicalHeight - _footerLogical));
        _ = NativeMethods.ShowWindow(control, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
    }

    private bool IsSelectedContentControl(nint control)
    {
        if (control == _contentTitle)
        {
            return true;
        }

        return _selectedCategory switch
        {
            0 => control is var value && (value == _themeSectionLabel
                || value == _themeLabel
                || value == _themeCombo
                || value == _appearanceHint
                || value == _fontSectionLabel
                || value == _textFontLabel
                || value == _textFontEdit
                || value == _textFontSizeLabel
                || value == _textFontSizeEdit
                || value == _monospaceFontLabel
                || value == _monospaceFontEdit
                || value == _fontSizeLabel
                || value == _fontSizeEdit
                || value == _fontHint
                || value == _windowSectionLabel
                || value == _windowHint),
            1 => control is var value && (value == _fontSectionLabel
                || value == _textFontLabel
                || value == _textFontEdit
                || value == _textFontSizeLabel
                || value == _textFontSizeEdit
                || value == _monospaceFontLabel
                || value == _monospaceFontEdit
                || value == _fontSizeLabel
                || value == _fontSizeEdit
                || value == _fontHint),
            2 => control is var value && (value == _monospaceFontLabel
                || value == _monospaceFontEdit
                || value == _fontSizeLabel
                || value == _fontSizeEdit
                || value == _fontHint),
            3 => control == _gitLabel || control == _gitExecutableEdit || control == _gitHint,
            4 => control == _terminalShellLabel
                || control == _terminalShellCombo
                || control == _terminalCustomLabel
                || control == _terminalCustomEdit
                || control == _terminalHint,
            _ => false,
        };
    }

    private int SetWrappedLabel(nint control, string text, int logicalWidth)
    {
        if (control == 0)
        {
            return 0;
        }

        if (text.Length == 0)
        {
            _ = NativeMethods.SetWindowText(control, text);
            return 0;
        }

        StringBuilder lines = new();
        StringBuilder current = new();
        foreach (char character in text)
        {
            if (character == '\r' || character == '\n')
            {
                if (lines.Length != 0) lines.AppendLine();
                lines.Append(current);
                current.Clear();
                continue;
            }

            current.Append(character);
            if (MeasureLogicalTextWidth(current.ToString(), NativeTheme.UiFont) > logicalWidth && current.Length > 1)
            {
                char last = current[^1];
                current.Length--;
                if (lines.Length != 0) lines.AppendLine();
                lines.Append(current);
                current.Clear();
                current.Append(last);
            }
        }

        if (current.Length != 0)
        {
            if (lines.Length != 0) lines.AppendLine();
            lines.Append(current);
        }

        _ = NativeMethods.SetWindowText(control, lines.ToString());
        return lines.Length == 0 ? 0 : lines.ToString().Split('\n').Length;
    }

    private static void MoveControl(nint control, int x, int y, int width, int height)
    {
        if (control == 0)
        {
            return;
        }

        _ = NativeMethods.MoveWindow(
            control,
            NativeTheme.Scale(x),
            NativeTheme.Scale(y),
            NativeTheme.Scale(Math.Max(0, width)),
            NativeTheme.Scale(Math.Max(0, height)),
            true);
    }

    private static void MoveInputControl(
        nint control,
        ref NativeMethods.Rectangle frame,
        int x,
        int y,
        int width,
        int height,
        bool isCombo = false)
    {
        int scaledX = NativeTheme.Scale(x);
        int scaledY = NativeTheme.Scale(y);
        int scaledWidth = NativeTheme.Scale(Math.Max(0, width));
        int scaledHeight = NativeTheme.Scale(Math.Max(0, height));
        frame = new()
        {
            Left = scaledX,
            Top = scaledY,
            Right = scaledX + scaledWidth,
            Bottom = scaledY + scaledHeight,
        };
        int inset = Math.Max(1, NativeTheme.Scale(1));
        int childHeight = Math.Max(0, scaledHeight - inset * 2);
        if (!isCombo)
        {
            nint dc = NativeMethods.GetDeviceContext(control);
            if (dc != 0)
            {
                nint previous = NativeMethods.SelectObject(dc, NativeTheme.UiFont);
                NativeMethods.Rectangle textBounds = new();
                int textHeight = NativeMethods.DrawText(dc, "Ag", 2, ref textBounds,
                    NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine);
                _ = NativeMethods.SelectObject(dc, previous);
                _ = NativeMethods.ReleaseDeviceContext(control, dc);
                childHeight = Math.Min(childHeight, Math.Max(1, textHeight + inset * 2));
            }
        }
        _ = NativeMethods.MoveWindow(control, scaledX + inset,
            scaledY + (scaledHeight - childHeight) / 2,
            Math.Max(0, scaledWidth - inset * 2), childHeight, true);
        ApplyRoundedRegion(
            control,
            Math.Max(0, scaledWidth - inset * 2),
            childHeight,
            Math.Max(1, NativeTheme.Scale(5)));
    }

    private nint CreateLabel(string text, int x, int y, int width = 150, int height = 24, bool centerImage = true)
    {
        uint style = NativeMethods.StaticLeft | (centerImage && height <= 30 ? NativeMethods.StaticCenterImage : 0);
        return CreateChild(NativeMethods.StaticClass, text, 0, style, x, y, width, height);
    }

    private nint CreateEdit(int identifier, string text, int x, int y, int width)
    {
        nint edit = CreateChild(
            NativeMethods.EditClass,
            text,
            identifier,
            NativeMethods.EditAutoHorizontalScroll,
            x,
            y,
            width,
            30);
        _ = NativeMethods.SendMessage(
            edit,
            NativeMethods.EditSetMargins,
            NativeMethods.EditMarginLeftRight,
            unchecked((nint)0x00080008));
        return edit;
    }

    private nint CreateChild(
        string className,
        string text,
        int identifier,
        uint specificStyle,
        int x,
        int y,
        int width,
        int height)
    {
        uint style = NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | specificStyle;
        if (!className.Equals(NativeMethods.StaticClass, StringComparison.Ordinal))
        {
            style |= NativeMethods.WindowStyleTabStop;
        }

        nint child = NativeMethods.CreateWindow(
            0,
            className,
            text,
            style,
            S(x),
            S(y),
            S(width),
            S(height),
            _handle,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (child == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.AppearanceControlCreateFailed);
        }

        _controls.Add(child);
        _ = NativeMethods.SendMessage(child, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        NativeTheme.ApplyToControl(child, _dark);
        return child;
    }

    private void HandleCommand(nuint wordParameter)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (command >= CategoryIdentifierBase && command < CategoryIdentifierBase + CategoryLabels.Length)
        {
            SelectCategory(command - CategoryIdentifierBase);
            return;
        }

        if (command == SearchIdentifier && notification == NativeMethods.EditNotificationChanged)
        {
            FilterCategories();
            return;
        }

        if (command == ThemeIdentifier && notification == NativeMethods.ComboBoxNotificationSelectionChanged)
        {
            PreviewTheme();
            return;
        }

        switch (command)
        {
            case CommandConfirm:
                Confirm();
                break;
            case CommandApply:
                Apply();
                break;
            case CommandCancel:
            case CommandClose:
                Close();
                break;
        }
    }

    private static void SetComboItemHeight(nint combo, int logicalHeight)
    {
        _ = NativeMethods.SendMessage(
            combo,
            NativeMethods.ComboBoxSetItemHeight,
            unchecked((nuint)(-1)),
            NativeTheme.Scale(logicalHeight));
        _ = NativeMethods.SendMessage(
            combo,
            NativeMethods.ComboBoxSetItemHeight,
            0,
            NativeTheme.Scale(logicalHeight));
    }

    private void SelectCategory(int category)
    {
        _selectedCategory = Math.Clamp(category, 0, CategoryLabels.Length - 1);
        // 切换分类后回到正文起点，避免上一分类的滚动偏移把新内容藏在固定操作栏后面。
        _contentScrollOffset = 0;
        _ = NativeMethods.SetWindowText(
            _contentTitle,
            _selectedCategory == 0 ? UiText.Appearance : CategoryLabels[_selectedCategory]);
        HideAllContent();
        switch (_selectedCategory)
        {
            case 0:
                ShowControls(
                    _themeSectionLabel,
                    _themeLabel,
                    _themeCombo,
                    _appearanceHint,
                    _textFontLabel,
                    _textFontEdit,
                    _textFontSizeLabel,
                    _textFontSizeEdit,
                    _monospaceFontLabel,
                    _monospaceFontEdit,
                    _fontSizeLabel,
                    _fontSizeEdit,
                    _fontHint,
                    _fontSectionLabel,
                    _windowSectionLabel,
                    _windowHint);
                break;
            case 1:
                ShowControls(
                    _textFontLabel,
                    _textFontEdit,
                    _textFontSizeLabel,
                    _textFontSizeEdit,
                    _monospaceFontLabel,
                    _monospaceFontEdit,
                    _fontSizeLabel,
                    _fontSizeEdit,
                    _fontHint);
                break;
            case 2:
                ShowControls(
                    _monospaceFontLabel,
                    _monospaceFontEdit,
                    _fontSizeLabel,
                    _fontSizeEdit,
                    _fontHint);
                break;
            case 3:
                ShowControls(_gitLabel, _gitExecutableEdit, _gitHint);
                break;
            case 4:
                ShowControls(
                    _terminalShellLabel,
                    _terminalShellCombo,
                    _terminalCustomLabel,
                    _terminalCustomEdit,
                    _terminalHint);
                break;
        }

        LayoutControls();

        foreach (nint button in _categoryButtons)
        {
            _ = NativeMethods.InvalidateRectangle(button, 0, true);
        }
        _ = NativeMethods.InvalidateRectangle(_contentTitle, 0, true);
    }

    private void HideAllContent()
    {
        foreach (nint control in new[]
        {
            _themeSectionLabel,
            _themeLabel,
            _themeCombo,
            _appearanceHint,
            _fontSectionLabel,
            _textFontLabel,
            _textFontEdit,
            _textFontSizeLabel,
            _textFontSizeEdit,
            _monospaceFontLabel,
            _monospaceFontEdit,
            _fontSizeLabel,
            _fontSizeEdit,
            _fontHint,
            _windowSectionLabel,
            _windowHint,
            _gitLabel,
            _gitExecutableEdit,
            _gitHint,
            _terminalShellLabel,
            _terminalShellCombo,
            _terminalCustomLabel,
            _terminalCustomEdit,
            _terminalHint,
        })
        {
            if (control != 0)
            {
                _ = NativeMethods.ShowWindow(control, NativeMethods.ShowHide);
            }
        }
    }

    private static void ShowControls(params nint[] controls)
    {
        foreach (nint control in controls)
        {
            if (control != 0)
            {
                _ = NativeMethods.ShowWindow(control, NativeMethods.ShowNormal);
            }
        }
    }

    private void FilterCategories()
    {
        string query = NativeMethods.GetWindowTextValue(_searchEdit).Trim();
        int firstVisible = -1;
        for (int index = 0; index < _categoryButtons.Count; index++)
        {
            bool visible = CategoryMatchesForTest(CategoryLabels[index], query);
            _ = NativeMethods.ShowWindow(
                _categoryButtons[index],
                visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
            if (visible && firstVisible < 0)
            {
                firstVisible = index;
            }
        }

        if (firstVisible >= 0
            && query.Length > 0
            && !CategoryLabels[_selectedCategory].Contains(query, StringComparison.CurrentCultureIgnoreCase))
        {
            SelectCategory(firstVisible);
        }
    }

    private void PreviewTheme()
    {
        string theme = ReadSelectedTheme();
        _dark = NativeTheme.IsDark(theme);
        ApplyAppearance(_dark);
        _previewSettings?.Invoke(_previewBaseSettings with { Theme = theme });
    }

    private void Apply()
    {
        ApplicationSettings? settings = BuildSettings();
        if (settings is null)
        {
            return;
        }

        _result = settings;
        _previewBaseSettings = settings;
        _previewSettings?.Invoke(settings);
        ApplyTypography(settings);
    }

    private void ApplyTypography(ApplicationSettings settings)
    {
        NativeTheme.ConfigureUiTypography(settings.TextFontFamily, settings.UiFontSize);
        nint previousHeading = _headingFont;
        _headingFont = NativeTheme.CreateOwnedUiFont(settings.TextFontFamily, settings.UiFontSize, 600);
        ApplyAppearance(NativeTheme.IsDark(settings.Theme));
        _ = NativeMethods.SendMessage(_contentTitle, NativeMethods.WindowMessageSetFont, unchecked((nuint)_headingFont), 1);
        if (previousHeading != 0)
        {
            _ = NativeMethods.DeleteObject(previousHeading);
        }
        LayoutControls();
    }

    private void Confirm()
    {
        ApplicationSettings? settings = BuildSettings();
        if (settings is null)
        {
            return;
        }

        _result = settings;
        _previewSettings?.Invoke(settings);
        Close();
    }

    private ApplicationSettings? BuildSettings()
    {
        string textFont = NativeMethods.GetWindowTextValue(_textFontEdit).Trim();
        string monospaceFont = NativeMethods.GetWindowTextValue(_monospaceFontEdit).Trim();
        string sizeText = NativeMethods.GetWindowTextValue(_fontSizeEdit);
        string textSizeText = NativeMethods.GetWindowTextValue(_textFontSizeEdit);
        if (textFont.Length == 0
            || monospaceFont.Length == 0
            || !double.TryParse(sizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out double fontSize)
            || !double.IsFinite(fontSize)
            || fontSize is < 9 or > 40
            || !double.TryParse(textSizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out double textFontSize)
            || !double.IsFinite(textFontSize)
            || textFontSize is < 9 or > 40)
        {
            _ = NativeMethods.MessageBox(
                _handle,
                UiText.AppearanceFontSizeError,
                UiText.AppName,
                NativeMethods.MessageBoxIconWarning);
            return null;
        }

        int shellIndex = checked((int)NativeMethods.SendMessage(
            _terminalShellCombo,
            NativeMethods.ComboBoxGetCurrentSelection,
            0,
            0));
        string terminalShell = ShellIdForIndexForTest(shellIndex);
        string? customCommand = NullIfWhiteSpace(NativeMethods.GetWindowTextValue(_terminalCustomEdit));
        if (terminalShell == TerminalShellIds.Custom && customCommand is null)
        {
            _ = NativeMethods.MessageBox(
                _handle,
                UiText.TerminalCustomCommandRequired,
                UiText.AppName,
                NativeMethods.MessageBoxIconWarning);
            return null;
        }

        return _initialSettings with
        {
            Theme = ReadSelectedTheme(),
            TextFontFamily = textFont,
            MonospaceFontFamily = monospaceFont,
            FontSize = fontSize,
            TextFontSize = textFontSize,
            GitExecutablePath = NullIfWhiteSpace(NativeMethods.GetWindowTextValue(_gitExecutableEdit)),
            TerminalShell = terminalShell,
            TerminalCustomCommand = customCommand,
        };
    }

    private string ReadSelectedTheme()
    {
        int themeIndex = checked((int)NativeMethods.SendMessage(
            _themeCombo,
            NativeMethods.ComboBoxGetCurrentSelection,
            0,
            0));
        return ThemeIdForIndexForTest(themeIndex);
    }

    private void ApplyAppearance(bool dark)
    {
        _dark = dark;
        NativeTheme.ApplyToWindow(_handle, dark);
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
        }

        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(dark).Panel);
        foreach (nint control in _controls)
        {
            NativeTheme.ApplyToControl(control, dark);
            _ = NativeMethods.InvalidateRectangle(control, 0, true);
        }
        _toolTip?.ApplyAppearance(dark);

        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
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
        int identifier = unchecked((int)item.ControlIdentifier);
        if (identifier >= CategoryIdentifierBase && identifier < CategoryIdentifierBase + CategoryLabels.Length)
        {
            return DrawCategory(item, identifier - CategoryIdentifierBase);
        }

        return identifier switch
        {
            CommandConfirm => NativeTheme.DrawFlatButton(parameter, _dark, emphasized: true),
            CommandApply or CommandCancel => NativeTheme.DrawFlatButton(parameter, _dark, outlined: true),
            CommandClose => NativeTheme.DrawFlatButton(parameter, _dark),
            ContentTitleIdentifier => DrawHeading(item),
            ThemeIdentifier => NativeComboBoxTheme.DrawItem(
                parameter,
                ThemeLabels,
                _dark),
            TerminalShellIdentifier => NativeComboBoxTheme.DrawItem(
                parameter,
                TerminalShellLabels,
                _dark),
            _ => false,
        };
    }

    private bool DrawHeading(NativeMethods.DrawItem item)
    {
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        NativeMethods.Rectangle rectangle = item.ItemRectangle;
        Fill(item.DeviceContext, rectangle, palette.Panel);
        _ = NativeMethods.SetBackgroundColor(item.DeviceContext, palette.Panel);
        _ = NativeMethods.SetBackgroundMode(item.DeviceContext, NativeMethods.BackgroundModeOpaque);
        _ = NativeMethods.SetTextColor(item.DeviceContext, palette.Text);
        nint previousFont = _headingFont == 0 ? 0 : NativeMethods.SelectObject(item.DeviceContext, _headingFont);
        string title = NativeMethods.GetWindowTextValue(item.Control);
        _ = NativeMethods.DrawText(
            item.DeviceContext,
            title,
            title.Length,
            ref rectangle,
            NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextNoPrefix);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(item.DeviceContext, previousFont);
        }

        return true;
    }

    private bool DrawCategory(NativeMethods.DrawItem item, int category)
    {
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        NativeMethods.Rectangle rectangle = item.ItemRectangle;
        bool selected = category == _selectedCategory;
        FillRounded(
            item.DeviceContext,
            rectangle,
            selected ? palette.AccentSoft : palette.Panel,
            S(5));
        rectangle.Left += S(category == 1 ? 22 : 10);
        rectangle.Right -= S(8);
        _ = NativeMethods.SetBackgroundMode(item.DeviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(item.DeviceContext, selected ? palette.Text : palette.Muted);
        nint font = category == 0 ? NativeTheme.UiMediumFont : NativeTheme.UiFont;
        nint previousFont = NativeMethods.SelectObject(item.DeviceContext, font);
        string label = NativeMethods.GetWindowTextValue(item.Control);
        _ = NativeMethods.DrawText(
            item.DeviceContext,
            label,
            label.Length,
            ref rectangle,
            NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextNoPrefix
                | NativeMethods.DrawTextEndEllipsis);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(item.DeviceContext, previousFont);
        }

        return true;
    }

    private void PaintWindow()
    {
        nint deviceContext = NativeMethods.BeginPaint(_handle, out NativeMethods.PaintStructure paint);
        if (deviceContext == 0)
        {
            return;
        }

        try
        {
            if (!NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
            {
                return;
            }

            NativeThemePalette palette = NativeTheme.Palette(_dark);
            Fill(deviceContext, client, palette.Panel);
            NativeMethods.Rectangle sideLine = new()
            {
                Left = S(_sidebarLogical),
                Top = S(_headerLogical),
                Right = S(_sidebarLogical + 1),
                Bottom = client.Bottom - S(_footerLogical),
            };
            Fill(deviceContext, sideLine, palette.Border);
            NativeMethods.Rectangle footerLine = new()
            {
                Left = 0,
                Top = client.Bottom - S(_footerLogical),
                Right = client.Right,
                Bottom = client.Bottom - S(_footerLogical - 1),
            };
            Fill(deviceContext, footerLine, palette.Border);
            NativeMethods.Rectangle title = new()
            {
                Left = S(26),
                Top = S(10),
                Right = Math.Max(S(400), client.Right - S(60)),
                Bottom = S(Math.Max(40, _headerLogical - 5)),
            };
            DrawText(deviceContext, UiText.SettingsTitle, title, palette.Text, NativeTheme.UiMediumFont);
            DrawHelpIcon(deviceContext, client.Bottom - S(_footerLogical), palette.Muted);
            PaintInputFrames(deviceContext, palette, client);
            if (_selectedCategory == 0)
            {
                foreach (int logicalTop in _appearanceSectionLines)
                {
                    int visibleTop = logicalTop - _contentScrollOffset;
                    if (NativeTheme.UiFontSizeForTest <= NativeTheme.UiFontLogicalSizeForTest
                        || IsContentFullyVisibleForTest(visibleTop, 1, _headerLogical, (int)Math.Round(NativeTheme.Unscale(client.Bottom)) - _footerLogical))
                    {
                        DrawAppearanceSectionLine(deviceContext, client, visibleTop, palette.Border);
                    }
                }
            }
        }
        finally
        {
            _ = NativeMethods.EndPaint(_handle, ref paint);
        }
    }

    private void PaintInputFrames(nint deviceContext, NativeThemePalette palette, NativeMethods.Rectangle client)
    {
        PaintRoundedInputIfVisible(deviceContext, _searchEditFrame, palette, _searchEdit, client);
        switch (_selectedCategory)
        {
            case 0:
                PaintRoundedInputIfVisible(deviceContext, _themeComboFrame, palette, _themeCombo, client);
                PaintRoundedInputIfVisible(deviceContext, _textFontEditFrame, palette, _textFontEdit, client);
                PaintRoundedInputIfVisible(deviceContext, _textFontSizeEditFrame, palette, _textFontSizeEdit, client);
                PaintRoundedInputIfVisible(deviceContext, _monospaceFontEditFrame, palette, _monospaceFontEdit, client);
                PaintRoundedInputIfVisible(deviceContext, _fontSizeEditFrame, palette, _fontSizeEdit, client);
                break;
            case 1:
                PaintRoundedInputIfVisible(deviceContext, _textFontEditFrame, palette, _textFontEdit, client);
                PaintRoundedInputIfVisible(deviceContext, _textFontSizeEditFrame, palette, _textFontSizeEdit, client);
                PaintRoundedInputIfVisible(deviceContext, _monospaceFontEditFrame, palette, _monospaceFontEdit, client);
                PaintRoundedInputIfVisible(deviceContext, _fontSizeEditFrame, palette, _fontSizeEdit, client);
                break;
            case 2:
                PaintRoundedInputIfVisible(deviceContext, _monospaceFontEditFrame, palette, _monospaceFontEdit, client);
                PaintRoundedInputIfVisible(deviceContext, _fontSizeEditFrame, palette, _fontSizeEdit, client);
                break;
            case 3:
                PaintRoundedInputIfVisible(deviceContext, _gitExecutableEditFrame, palette, _gitExecutableEdit, client);
                break;
            case 4:
                PaintRoundedInputIfVisible(deviceContext, _terminalShellComboFrame, palette, _terminalShellCombo, client);
                PaintRoundedInputIfVisible(deviceContext, _terminalCustomEditFrame, palette, _terminalCustomEdit, client);
                break;
        }
    }

    private void PaintRoundedInputIfVisible(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        NativeThemePalette palette,
        nint control,
        NativeMethods.Rectangle client)
    {
        if (NativeTheme.UiFontSizeForTest > NativeTheme.UiFontLogicalSizeForTest
            && !IsContentFullyVisibleForTest(
                (int)Math.Round(NativeTheme.Unscale(rectangle.Top)),
                (int)Math.Round(NativeTheme.Unscale(rectangle.Bottom - rectangle.Top)),
                _headerLogical,
                (int)Math.Round(NativeTheme.Unscale(client.Bottom)) - _footerLogical))
        {
            return;
        }

        PaintRoundedInput(deviceContext, rectangle, palette, control);
    }

    private static void PaintRoundedInput(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        NativeThemePalette palette,
        nint control)
    {
        if (rectangle.Right <= rectangle.Left || rectangle.Bottom <= rectangle.Top)
        {
            return;
        }

        uint border = NativeMethods.GetFocus() == control ? palette.Accent : palette.BorderStrong;
        FillRounded(deviceContext, rectangle, border, NativeTheme.Scale(6));
        NativeMethods.Rectangle inner = rectangle;
        int inset = Math.Max(1, NativeTheme.Scale(1));
        inner.Left += inset;
        inner.Top += inset;
        inner.Right -= inset;
        inner.Bottom -= inset;
        FillRounded(deviceContext, inner, palette.Panel, NativeTheme.Scale(5));
    }

    private static void DrawAppearanceSectionLine(
        nint deviceContext,
        NativeMethods.Rectangle client,
        int logicalTop,
        uint color)
    {
        NativeMethods.Rectangle line = new()
        {
            Left = S(390),
            Top = S(logicalTop),
            Right = Math.Max(S(390), client.Right - S(24)),
            Bottom = S(logicalTop + 1),
        };
        Fill(deviceContext, line, color);
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
        nint pen = NativeMethods.CreatePen(
            NativeMethods.PenStyleSolid,
            Math.Max(1, S(1)),
            color);
        nint previousPen = pen == 0
            ? 0
            : NativeMethods.SelectObject(deviceContext, pen);
        nint previousBrush = NativeMethods.SelectObject(
            deviceContext,
            NativeMethods.GetStockObject(NativeMethods.NullBrush));
        _ = NativeMethods.DrawEllipse(
            deviceContext,
            circle.Left,
            circle.Top,
            circle.Right,
            circle.Bottom);
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

    private nint HitTest()
    {
        if (!NativeMethods.GetCursorPosition(out NativeMethods.Point point)
            || !NativeMethods.ScreenToClient(_handle, ref point))
        {
            return NativeMethods.HitTestClient;
        }

        return point.Y < S(_headerLogical) && point.X < Math.Max(0, _dialogWidth - S(54))
            ? NativeMethods.HitTestCaption
            : NativeMethods.HitTestClient;
    }

    private static void ApplyRoundedRegion(nint window, int width, int height, int radius)
    {
        if (window == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        nint region = NativeMethods.CreateRoundRectangleRegion(0, 0, width + 1, height + 1, radius, radius);
        if (region != 0 && NativeMethods.SetWindowRegion(window, region, true) == 0)
        {
            _ = NativeMethods.DeleteObject(region);
        }
    }

    private void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        nint handle = _handle;
        NativeMethods.WakeWindowMessageLoop(handle);
        _handle = 0;
        lock (InstancesGate)
        {
            Instances.Remove(handle);
        }

        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }

        _toolTip?.Dispose();
        _toolTip = null;

        if (_headingFont != 0)
        {
            _ = NativeMethods.DeleteObject(_headingFont);
            _headingFont = 0;
        }

        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }

    }

    private static string? NullIfWhiteSpace(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static int S(int logicalPixels)
    {
        return NativeTheme.Scale(logicalPixels);
    }

    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
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

    private static void FillRounded(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        uint color,
        int radius)
    {
        if (NativeGdiPlusDrawing.FillRoundedRectangle(deviceContext, rectangle, color, radius))
        {
            return;
        }

        Fill(deviceContext, rectangle, color);
    }

    internal static bool CategoryMatchesForTest(string label, string query)
    {
        return query.Length == 0 || label.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    internal static int ThemeIndexForTest(string theme)
    {
        return theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? 1
            : theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
    }

    internal static string ThemeIdForIndexForTest(int index)
    {
        return index switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "System",
        };
    }

    internal static int ShellIndexForTest(string shell)
    {
        return TerminalShellIds.Normalize(shell) switch
        {
            TerminalShellIds.PowerShell7 => 1,
            TerminalShellIds.CommandPrompt => 2,
            TerminalShellIds.GitBash => 3,
            TerminalShellIds.Wsl => 4,
            TerminalShellIds.Custom => 5,
            _ => 0,
        };
    }

    internal static string ShellIdForIndexForTest(int index)
    {
        return index switch
        {
            1 => TerminalShellIds.PowerShell7,
            2 => TerminalShellIds.CommandPrompt,
            3 => TerminalShellIds.GitBash,
            4 => TerminalShellIds.Wsl,
            5 => TerminalShellIds.Custom,
            _ => TerminalShellIds.WindowsPowerShell,
        };
    }

    private static void DrawText(
        nint deviceContext,
        string text,
        NativeMethods.Rectangle rectangle,
        uint color,
        nint font)
    {
        nint previousFont = font == 0 ? 0 : NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(deviceContext, color);
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            text.Length,
            ref rectangle,
            NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextNoPrefix);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
    }
}
