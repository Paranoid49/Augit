using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Augit.Core.Documents;
using Augit.Core.Git;
using Augit.Infrastructure.Interop;
using Augit.Infrastructure.Settings;
using Microsoft.Web.WebView2.Core;

namespace Augit.App;

internal sealed partial class NativeDocumentView : IDisposable
{
    private const string WindowClassName = "Augit.DocumentView.Native";
    private static int ToolbarHeight => NativeTheme.ContentHeight(36, 8);
    private static int FindOverlayHeight => NativeTheme.ContentHeight(42, 16);
    private static int FindEditHeight => NativeTheme.ContentHeight(30, 4);
    private static int FindOverlayPadding => NativeTheme.Scale(7);
    private static int FindOverlayGap => NativeTheme.Scale(3);
    private static int FindButtonSize => NativeTheme.Scale(28);
    private static int FindStatusPreferredWidth => NativeTheme.Scale(48);
    private static int FindEditMinimumWidth => NativeTheme.Scale(80);
    private static int MarkdownSplitterWidth => NativeTheme.Scale(6);
    private static int ModeButtonWidth => NativeTheme.Scale(26);
    private static int ModeButtonHeight => NativeTheme.Scale(26);
    private static int ModeSegmentPadding => 0;
    private static int ModeSegmentGap => 0;
    private const int CommandOriginal = 1;
    private const int CommandAlternative = 2;
    private const int CommandSplit = 3;
    private const int CommandFind = 4;
    private const int CommandGoToLine = 5;
    private const int CommandWordWrap = 6;
    private const int CommandWhitespace = 7;
    private const int CommandFindPrevious = 9;
    private const int CommandFindNext = 10;
    private const int CommandCloseFind = 11;
    private const int CommandMatchCase = 12;
    private const int CommandWholeWord = 13;
    private const int CommandRegularExpression = 14;
    private const int CommandMore = 15;
    private const int CommandImageZoomOut = 16;
    private const int CommandImageZoomIn = 17;
    private const int CommandImageFitToArea = 18;
    private const int CommandJsonError = 19;
    private const int CommandCloseBlame = 21;
    private const int BreadcrumbIdentifier = 30;
    private const int PreviewStatusIdentifier = 31;
    private const int ImageSizeIdentifier = 32;
    private const int ImageZoomIdentifier = 33;
    private const int TargetPathIdentifier = 34;
    private const int FindOverlayIdentifier = 35;
    private const int FindStatusIdentifier = 36;
    private const int BlameCountIdentifier = 37;
    private const int FindEditIdentifier = 20;
    private const nuint FindEditSubclassIdentifier = 1;
    private const int EditNotificationChanged = 0x0300;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeDocumentView> Instances = [];
    private static readonly Dictionary<nint, NativeDocumentView> FindEditInstances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static readonly NativeMethods.SubclassProcedure FindEditProcedure = HandleFindEditMessage;
    private static bool _classRegistered;
    private readonly string _workspaceRoot;
    private readonly Action<string> _setStatus;
    private readonly Action<string, int?, string?> _openLinkedFile;
    private readonly List<nint> _toolbarControls = [];
    private readonly List<nint> _imageToolbarControls = [];
    private ApplicationSettings _settings;
    private DocumentReadResult _result;
    private ScintillaControl? _originalEditor;
    private ScintillaControl? _formattedEditor;
    private JsonDisplayResult? _formattedJson;
    private NativeImageView? _imageView;
    private NativeDocumentInfoView? _infoView;
    private MarkdownWebViewHost? _markdownPreview;
    private CancellationTokenSource? _markdownPreviewCancellation;
    private nint _previewErrorLabel;
    private nint _previewStatusLabel;
    private nint _targetLabel;
    private NativeMethods.Rectangle _modeSegmentRectangle;
    private bool _modeSegmentVisible;
    private nint _originalButton;
    private nint _alternativeButton;
    private nint _splitButton;
    private nint _findButton;
    private nint _goToButton;
    private nint _wordWrapButton;
    private nint _whitespaceButton;
    private nint _findOverlay;
    private nint _findEdit;
    private NativeMethods.Rectangle _findEditFrame;
    private nint _findPreviousButton;
    private nint _findNextButton;
    private nint _findCloseButton;
    private nint _matchCaseButton;
    private nint _wholeWordButton;
    private nint _regularExpressionButton;
    private nint _findStatus;
    private nint _blameCountLabel;
    private nint _blameCloseButton;
    private nint _breadcrumbLabel;
    private nint _moreButton;
    private nint _jsonErrorButton;
    private nint _imageSizeLabel;
    private nint _imageZoomOutButton;
    private nint _imageZoomLabel;
    private nint _imageZoomInButton;
    private nint _imageFitButton;
    private NativeToolTip? _imageToolTip;
    private NativeToolTip? _modeToolTip;
    private NativeToolTip? _documentToolTip;
    private NativeContextMenu? _contextMenu;
    private nint _controlBrush;
    private bool _findVisible;
    private bool _showAlternative;
    private bool _draggingMarkdownSplitter;
    private int _markdownSplitterGrabOffset;
    private double _markdownSplitRatio = 0.5d;
    private DocumentDisplayMode _displayMode;
    private int _markdownPreviewVersion;
    private bool _wordWrap;
    private bool _showWhitespace;
    private bool _matchCase;
    private bool _wholeWord;
    private bool _regularExpression;
    private int _findPosition;
    private (int Start, int Length)? _lastFindMatch;
    private bool _findComposing;
    private bool _showBlame;
    private int _findStatusWidth;
    private bool _findStatusTipAdded;
    private (string Source, string Query, bool Case, bool Whole, bool Regex, string Text)? _findCountCache;
    private List<(int Start, int Length)> _findMatches = [];
    private int _findMatchIndex = -1;
    private bool _findHighlightsVisible;
    internal int FindStatusScanCountForTest { get; private set; }
    private string? _pendingAnchor;
    private string? _markdownPreviewError;
    private Action<string>? _openBlameCommit;
    private bool _hasBounds;
    private (int X, int Y, int Width, int Height) _bounds;
    private bool _disposed;

    internal NativeDocumentView(
        nint parent,
        string workspaceRoot,
        DocumentReadResult result,
        ApplicationSettings settings,
        Action<string> setStatus,
        Action<string, int?, string?> openLinkedFile,
        NativeImageDecoder? imageDecoder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(setStatus);
        ArgumentNullException.ThrowIfNull(openLinkedFile);
        _workspaceRoot = workspaceRoot;
        _result = result;
        _settings = settings;
        _setStatus = setStatus;
        _openLinkedFile = openLinkedFile;
        _imageDecoder = imageDecoder ?? NativeImageDecoder.Shared;
        EnsureWindowClass();
        Handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            string.Empty,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleClipChildren
                | NativeMethods.WindowStyleClipSiblings,
            0,
            0,
            0,
            0,
            parent,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (Handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.DocumentViewCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(Handle, this);
        }

        CreateToolbar();
        CreateImageToolbar();
        CreateFindBar();
        CreateBlameToolbar();
        CreateToolTips();
        ApplyResult();
        ApplyAppearance(settings);
        Layout();
    }

    internal nint Handle { get; private set; }

    internal string Path => _result.RequestedPath;

    internal string ReadStatusText { get; private set; } = string.Empty;

    internal bool IsMarkdownPreviewReady => _markdownPreview is not null && !_markdownPreviewDirty
        && _markdownPreviewCancellation is null && _displayMode != DocumentDisplayMode.Original;

    internal bool IsMarkdownPreviewLoading => _markdownPreviewCancellation is not null;

    internal string? MarkdownPreviewError => _markdownPreviewError;

    internal bool IsImagePreviewReady => _imageView?.BitmapWidth > 0;

    internal int ImageZoomPercentageForTest => _imageView?.ZoomPercentage ?? 0;

    internal bool ImageFitToAreaForTest => _imageView?.IsFitToArea == true;

    internal bool ImageToolbarWithinClientBoundsForTest => _imageView is not null
        && _imageToolbarControls.All(IsControlWithinToolbar);

    internal bool InfoPageHasSinglePrimaryActionForTest => _infoView?.HasSinglePrimaryActionForTest == true;

    internal bool InfoPageActionWithinBoundsForTest => _infoView?.OpenButtonWithinBoundsForTest == true;

    internal bool InfoPageActionHasFocusForTest => _infoView?.PrimaryActionHasFocusForTest == true;

    internal bool ZoomImageInForTest()
    {
        if (_imageView is null)
        {
            return false;
        }

        int before = _imageView.ZoomPercentage;
        _imageView.ZoomIn();
        UpdateImageToolbarState();
        return _imageView.ZoomPercentage > before;
    }

    internal void FitImageToAreaForTest()
    {
        _imageView?.FitToArea();
        UpdateImageToolbarState();
    }

    internal string? CurrentText => _result.Text;

    internal bool IsTextReadOnly => _originalEditor?.IsReadOnly != false;

    internal DocumentReadStatus Status => _result.Status;

    internal DocumentLineEndings LineEndings => _result.LineEndings;
    internal string TypeName => _result.Classification.TypeName;

    internal event Action? ReadResultChanged;

    internal DocumentKind Kind => _result.Classification.Kind;

    internal bool IsShowingAlternative => _showAlternative;

    internal bool IsShowingBlame => _originalEditor?.BlameVisible == true;

    internal int SelectedBlameLineForTest => _originalEditor?.SelectedBlameLineForTest ?? -1;

    internal bool BlameToolbarVisibleForTest => _showBlame
        && NativeMethods.IsWindowVisible(_blameCountLabel)
        && NativeMethods.IsWindowVisible(_blameCloseButton);

    internal string BlameCountTextForTest => NativeMethods.GetWindowTextValue(_blameCountLabel);

    internal nint BlameCloseButtonForTest => _blameCloseButton;

    internal bool BlameToolbarWithinClientBoundsForTest => _showBlame
        && IsControlWithinToolbar(_blameCloseButton) && IsControlWithinToolbar(_blameCountLabel);

    internal bool ModeToolbarWithinClientBoundsForTest
    {
        get
        {
            nint[] controls = _result.Classification.Kind == DocumentKind.Markdown
                ? [_originalButton, _splitButton, _alternativeButton, _moreButton]
                : [_originalButton, _alternativeButton, _moreButton];
            return _result.Classification.Kind is DocumentKind.Json or DocumentKind.Markdown
                && controls.All(IsControlWithinToolbar)
                && IsModeSegmentWithinToolbar();
        }
    }

    internal bool ModeSegmentPaintedByParentForTest => _modeSegmentVisible
        && _modeSegmentRectangle.Right > _modeSegmentRectangle.Left
        && _modeSegmentRectangle.Bottom > _modeSegmentRectangle.Top;

    internal bool TextToolbarWithinClientBoundsForTest =>
        _result.Classification.Kind == DocumentKind.Text
        && new[] { _wordWrapButton, _whitespaceButton, _findButton, _goToButton }
            .All(IsControlWithinToolbar);

    internal bool FindOverlayVisibleForTest => _findVisible
        && _findOverlay != 0
        && NativeMethods.IsWindowVisible(_findOverlay);

    internal bool FindOverlayWithinClientBoundsForTest => _findVisible
        && new[]
        {
            _findOverlay,
            _findEdit,
            _matchCaseButton,
            _wholeWordButton,
            _regularExpressionButton,
            _findStatus,
            _findPreviousButton,
            _findNextButton,
            _findCloseButton,
        }.All(IsControlWithinClient);

    internal bool DocumentToolTipsCreatedForTest => _documentToolTip is { IsCreatedForTest: true }
        && new[]
        {
            _breadcrumbLabel, _wordWrapButton, _whitespaceButton, _findButton, _goToButton, _moreButton,
            _matchCaseButton, _wholeWordButton, _regularExpressionButton,
            _findPreviousButton, _findNextButton, _findCloseButton,
            _blameCountLabel, _blameCloseButton,
        }.All(_documentToolTip.ContainsForTest);

    internal int ContentTopForTest => GetContentTop();

    internal string FindStatusForTest => NativeMethods.GetWindowTextValue(_findStatus);

    internal bool ContainsWindow(nint window)
    {
        return NativeFocusNavigation.ContainsWindow(Handle, window);
    }

    internal bool FindEditHasFocusForTest => NativeMethods.GetFocus() == _findEdit;

    internal bool FindMatchCaseHasFocusForTest => NativeMethods.GetFocus() == _matchCaseButton;

    internal bool FindWholeWordHasFocusForTest => NativeMethods.GetFocus() == _wholeWordButton;

    internal bool FindPreviousHasFocusForTest => NativeMethods.GetFocus() == _findPreviousButton;

    internal bool FindNextHasFocusForTest => NativeMethods.GetFocus() == _findNextButton;

    internal bool FindCloseHasFocusForTest => NativeMethods.GetFocus() == _findCloseButton;

    internal bool WordWrapHasFocusForTest => NativeMethods.GetFocus() == _wordWrapButton;

    internal bool WhitespaceHasFocusForTest => NativeMethods.GetFocus() == _whitespaceButton;

    internal bool FindButtonHasFocusForTest => NativeMethods.GetFocus() == _findButton;

    internal string NavigationFocusForTest
    {
        get
        {
            nint focus = NativeMethods.GetFocus();
            if (focus == _wordWrapButton) return "换行";
            if (focus == _whitespaceButton) return "空白";
            if (focus == _findButton) return "查找";
            if (focus == _goToButton) return "跳转";
            if (focus == _originalButton) return "原文";
            if (focus == _splitButton) return "对照";
            if (focus == _alternativeButton) return "替代";
            if (focus == _moreButton) return "更多";
            if (ActiveEditor is { Handle: var editor } && focus == editor) return "正文";
            return focus == Handle ? "文档视图" : $"未知({focus})";
        }
    }

    internal string NavigationControlsStateForTest
    {
        get
        {
            nint[] controls = BuildNavigationControls();
            return string.Join(
                ",",
                controls.Select(control =>
                    $"{control}:{NativeMethods.IsWindowVisible(control)}/{NativeMethods.IsWindowEnabled(control)}"));
        }
    }

    internal bool ContainsFindWindow(nint window)
    {
        return _findVisible
            && window != 0
            && (window == _findEdit
                || window == _matchCaseButton
                || window == _wholeWordButton
                || window == _regularExpressionButton
                || window == _findPreviousButton
                || window == _findNextButton
                || window == _findCloseButton);
    }

    internal bool IsFindInputComposing => _findComposing && NativeMethods.GetFocus() == _findEdit;

    internal bool HandleFindShortcut(NativeMethods.Message message)
    {
        nint focus = NativeMethods.GetFocus();
        if (message.MessageId != NativeMethods.WindowMessageKeyDown || message.Window != focus
            || !ContainsFindWindow(focus) || focus == _findEdit
            || !NativeMethods.IsWindowEnabled(focus)
            || message.WordParameter != NativeMethods.VirtualKeyEnter)
            return false;
        _ = NativeMethods.SendMessage(focus, 0x00F5, 0, 0);
        return true;
    }

    internal bool HandleFindTabNavigation(bool backwards)
    {
        if (!_findVisible)
        {
            return false;
        }

        return NativeFocusNavigation.MoveWithinRegion(
            [
                _findEdit,
                _matchCaseButton,
                _wholeWordButton,
                _regularExpressionButton,
                _findPreviousButton,
                _findNextButton,
                _findCloseButton,
            ],
            NativeMethods.GetFocus(),
            backwards);
    }

    internal bool FocusNavigationControl(bool backwards)
    {
        return NativeFocusNavigation.MoveWithinRegion(
            BuildNavigationControls(),
            0,
            backwards);
    }

    internal bool HandleTabNavigation(bool backwards, nint documentTabs)
    {
        nint[] controls = BuildNavigationControls()
            .Where(IsFocusable)
            .Distinct()
            .ToArray();
        if (controls.Length == 0)
        {
            return false;
        }

        nint focus = NativeMethods.GetFocus();
        int currentIndex = Array.FindIndex(
            controls,
            control => control == focus
                || (focus != 0 && NativeMethods.IsChild(control, focus)));
        if (currentIndex < 0)
        {
            return NativeFocusNavigation.MoveWithinRegion(controls, focus, backwards);
        }

        if (backwards && currentIndex == 0)
        {
            _ = NativeMethods.SetFocus(documentTabs);
            return NativeMethods.GetFocus() == documentTabs;
        }

        if (!backwards && currentIndex == controls.Length - 1)
        {
            _ = NativeMethods.SetFocus(documentTabs);
            return NativeMethods.GetFocus() == documentTabs;
        }

        int nextIndex = backwards ? currentIndex - 1 : currentIndex + 1;
        _ = NativeMethods.SetFocus(controls[nextIndex]);
        return NativeMethods.GetFocus() == controls[nextIndex];
    }

    private static bool IsFocusable(nint control)
    {
        return control != 0
            && NativeMethods.IsWindow(control)
            && NativeMethods.IsWindowVisible(control)
            && NativeMethods.IsWindowEnabled(control);
    }

    private nint[] BuildNavigationControls()
    {
        // 键盘顺序跟随画面从左到右，不能依赖控件创建顺序。
        List<nint> controls = [_originalButton, _splitButton, _alternativeButton,
            _wordWrapButton, _whitespaceButton, _findButton, _goToButton, _moreButton, _blameCloseButton];
        if (_imageView is not null)
        {
            controls.Add(_imageZoomOutButton);
            controls.Add(_imageZoomInButton);
            controls.Add(_imageFitButton);
        }

        if (_infoView is not null)
        {
            controls.Add(_infoView.Handle);
        }

        controls.Add(_jsonErrorButton);

        if (ActiveEditor is { Handle: var editorHandle })
        {
            controls.Add(editorHandle);
        }
        else if (_imageView is { Handle: var imageHandle })
        {
            controls.Add(imageHandle);
        }
        else if (_markdownPreview is { Handle: var previewHandle })
        {
            controls.Add(previewHandle);
        }

        return controls.ToArray();
    }

    internal static int ModeSegmentWidthForTest(int buttonCount)
    {
        return CalculateModeSegmentWidth(buttonCount);
    }

    internal static (uint Background, uint Icon) ModeToolbarButtonColorsForTest(
        bool dark,
        bool selected,
        bool disabled,
        bool pressed)
    {
        return ResolveModeToolbarButtonColors(dark, selected, disabled, pressed);
    }

    internal int MarkdownBrowserProcessId => _markdownPreview?.BrowserProcessId ?? 0;

    internal void SetBounds(int x, int y, int width, int height)
    {
        (int X, int Y, int Width, int Height) next = (x, y, Math.Max(0, width), Math.Max(0, height));
        if (!ShouldApplyBoundsForTest(_hasBounds, _bounds, next))
        {
            return;
        }

        _hasBounds = true;
        _bounds = next;
        // 主窗口局部刷新会重复下发相同边界，跳过无效 MoveWindow 可避免正文控件再次布局。
        _ = NativeMethods.MoveWindow(Handle, next.X, next.Y, next.Width, next.Height, true);
    }

    internal static bool ShouldApplyBoundsForTest(
        bool hasBounds,
        (int X, int Y, int Width, int Height) current,
        (int X, int Y, int Width, int Height) next)
    {
        return !hasBounds || current != next;
    }

    internal static int CalculateMarkdownSplitSourceWidthForTest(int width)
    {
        return CalculateMarkdownSplitSourceWidth(width, 0.5d);
    }

    internal static int CalculateMarkdownSplitSourceWidthForTest(int width, double ratio)
    {
        return CalculateMarkdownSplitSourceWidth(width, ratio);
    }

    internal static int MarkdownSplitterWidthForTest => MarkdownSplitterWidth;

    internal int MarkdownSplitSourceWidthForTest => NativeMethods.GetClientRectangle(
        Handle,
        out NativeMethods.Rectangle client)
            ? GetMarkdownSplitSourceWidth(client.Right - client.Left)
            : 0;

    internal bool DragMarkdownSplitterForTest(int delta)
    {
        if (!IsMarkdownSplitMode()
            || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client))
        {
            return false;
        }

        int before = GetMarkdownSplitSourceWidth(client.Right - client.Left);
        int startX = before + MarkdownSplitterWidth / 2;
        int y = Math.Min(client.Bottom - 1, GetContentTop() + NativeTheme.Scale(20));
        nint start = PackClientPoint(startX, y);
        nint end = PackClientPoint(startX + delta, y);
        if (!BeginMarkdownSplitterDrag(start))
        {
            return false;
        }

        UpdateMarkdownSplitterDrag(end);
        EndMarkdownSplitterDrag(end);
        int after = GetMarkdownSplitSourceWidth(client.Right - client.Left);
        return after != before && NativeMethods.GetCapture() != Handle;
    }

    internal static int ToolbarHeightForTest => ToolbarHeight;

    internal static int FindOverlayHeightForTest => FindOverlayHeight;

    internal static int FindOverlayButtonSizeForTest(int width) => GetFindOverlayButtonSize(width);

    internal static (int X, int Y, int Width, int Height, int EditWidth, int StatusWidth)
        CalculateFindOverlayLayoutForTest(int width, int contentTop)
    {
        return CalculateFindOverlayLayout(width, contentTop);
    }

    internal void SetVisible(bool visible)
    {
        if (!visible) ClearToolbarHover(_hoveredToolbarButton);
        if (!visible)
        {
            ResetFindComposition();
            CancelFindWork();
            CancelMarkdownSplitterDrag();
        }
        _imageView?.SetVisible(visible);
        _ = NativeMethods.ShowWindow(Handle, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        if (!visible) CancelMarkdownPreviewWork();
        _markdownPreview?.SetVisible(visible && _displayMode != DocumentDisplayMode.Original);
        if (visible)
        {
            Layout();
            if (_findVisible) UpdateFindStatus();
        }

        if (visible
            && _result.Classification.Kind == DocumentKind.Markdown
            && (_markdownPreview is null || _markdownPreviewDirty)
            && _markdownPreviewCancellation is null
            && string.IsNullOrEmpty(_markdownPreviewError)
            && _displayMode is DocumentDisplayMode.Preview or DocumentDisplayMode.Split)
        {
            // Markdown 预览只在标签首次可见时启动，避免隐藏标签并发创建 WebView2。
            _ = SetMarkdownModeAsync(_displayMode);
        }
    }

    internal void ApplyAppearance(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool markdownAppearanceChanged = _settings.Theme != settings.Theme
            || !string.Equals(_settings.TextFontFamily, settings.TextFontFamily, StringComparison.Ordinal)
            || !string.Equals(_settings.MonospaceFontFamily, settings.MonospaceFontFamily, StringComparison.Ordinal)
            || _settings.FontSize != settings.FontSize
            || _settings.UiFontSize != settings.UiFontSize;
        _settings = settings;
        bool dark = NativeTheme.IsDark(settings.Theme);
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
        }

        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(dark).Panel);
        _originalEditor?.ApplyAppearance(settings.MonospaceFontFamily, settings.FontSize, dark);
        _formattedEditor?.ApplyAppearance(settings.MonospaceFontFamily, settings.FontSize, dark);
        _imageView?.ApplyAppearance(dark);
        _infoView?.ApplyAppearance(dark);
        _imageToolTip?.ApplyAppearance(dark);
        _modeToolTip?.ApplyAppearance(dark);
        _documentToolTip?.ApplyAppearance(dark);
        foreach (nint control in _toolbarControls.Concat(_imageToolbarControls).Concat(new[]
        {
            _breadcrumbLabel,
            _findEdit,
            _matchCaseButton,
            _wholeWordButton,
            _regularExpressionButton,
            _findPreviousButton,
            _findNextButton,
            _findCloseButton,
            _findStatus,
            _blameCountLabel,
            _blameCloseButton,
            _targetLabel,
            _jsonErrorButton,
            _previewErrorLabel,
            _previewStatusLabel,
            _findOverlay,
        }))
        {
            NativeTheme.ApplyToControl(control, dark);
        }

        InvalidateToolbar();
        _ = NativeMethods.InvalidateRectangle(Handle, 0, true);
        MeasureFindStatusWidth();
        Layout();
        if (_result.Classification.Kind == DocumentKind.Markdown && markdownAppearanceChanged)
        {
            _markdownPreviewDirty = true;
            if (_displayMode != DocumentDisplayMode.Original && NativeMethods.IsWindowVisible(Handle))
                _ = SetMarkdownModeAsync(_displayMode, forceReload: true);
            else CancelMarkdownPreviewWork();
        }
    }

    internal async Task ReloadAsync(DocumentReadResult result)
    {
        if (_disposed) return;
        try { await ReloadContentAsync(result); }
        finally { if (!_disposed) ReadResultChanged?.Invoke(); }
    }

    private async Task ReloadContentAsync(DocumentReadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (TryReloadNativeText(result))
        {
            if (_result.Classification.Kind == DocumentKind.Markdown && _markdownPreviewDirty
                && _markdownPreviewCancellation is null
                && _displayMode != DocumentDisplayMode.Original && NativeMethods.IsWindowVisible(Handle))
                await SetMarkdownModeAsync(_displayMode, forceReload: true);
            return;
        }
        if (TryReloadImage(result)) { await ImageLoadCompletion; return; }
        bool preserveMarkdownMode = _result.Classification.Kind == DocumentKind.Markdown
            && result.Classification.Kind == DocumentKind.Markdown;
        DocumentDisplayMode previousMode = _displayMode;
        _result = result;
        ApplyResult();
        if (preserveMarkdownMode)
        {
            _displayMode = previousMode;
            _showAlternative = previousMode != DocumentDisplayMode.Original;
        }
        ApplyAppearance(_settings);
        Layout();
        await ImageLoadCompletion;
        if (_result.Classification.Kind == DocumentKind.Markdown
            && _displayMode is DocumentDisplayMode.Preview or DocumentDisplayMode.Split
            && NativeMethods.IsWindowVisible(Handle))
        {
            await SetMarkdownModeAsync(_displayMode);
        }
    }

    private bool TryReloadNativeText(DocumentReadResult result)
    {
        if (_result.Status != DocumentReadStatus.TextReady || result.Status != DocumentReadStatus.TextReady
            || _originalEditor is null || _result.Classification.Kind != result.Classification.Kind
            || result.Classification.Kind is not (DocumentKind.Text or DocumentKind.Json or DocumentKind.Markdown)
            || !_result.RequestedPath.Equals(result.RequestedPath, StringComparison.OrdinalIgnoreCase)
            || !_result.ResolvedPath.Equals(result.ResolvedPath, StringComparison.OrdinalIgnoreCase)) return false;

        if (_result == result && !_originalEditor.BlameVisible) return true;
        bool contentChanged = !string.Equals(_result.Text, result.Text, StringComparison.Ordinal);
        if (contentChanged) CancelFindWork();
        _result = result;
        ReadStatusText = result.Message.Length == 0 ? result.Classification.TypeName : result.Message;
        ClearBlame(focus: false);
        if (contentChanged) _originalEditor.SetTextContent(result.Text ?? string.Empty);
        if (result.Classification.Kind == DocumentKind.Markdown && contentChanged)
        {
            _markdownPreviewDirty = true;
            CancelMarkdownPreviewWork();
        }
        if (result.Classification.Kind == DocumentKind.Json && _formattedEditor is not null)
        {
            JsonDisplayResult formatted = !contentChanged && _formattedJson is not null
                ? _formattedJson : JsonDisplayFormatter.Format(result.Text ?? string.Empty);
            _formattedJson = formatted;
            if (contentChanged) _formattedEditor.SetTextContent(formatted.DisplayText);
            if (!formatted.IsValid)
            {
                ReadStatusText = UiText.JsonError(formatted.ErrorLine, formatted.ErrorColumn);
                _setStatus(ReadStatusText);
                if (_showAlternative)
                {
                    bool transferFocus = NativeMethods.GetFocus() == _formattedEditor.Handle;
                    _showAlternative = false;
                    _formattedEditor.SetVisible(false);
                    _originalEditor.SetVisible(true);
                    InvalidateToolbar();
                    if (transferFocus) _ = NativeMethods.SetFocus(_originalEditor.Handle);
                }
            }
            UpdateJsonErrorButton(formatted);
        }
        // 同类型文本复用正文；错误条仅调整文档内部布局，保留模式与查找上下文。
        _findPosition = Math.Clamp(_findPosition, 0, ActiveText.Length);
        UpdateFindStatus(preservePosition: true);
        return true;
    }

    internal void ShowFind()
    {
        if (_originalEditor is null && _formattedEditor is null)
        {
            return;
        }

        if (_result.Classification.Kind == DocumentKind.Markdown && _displayMode == DocumentDisplayMode.Preview)
        {
            ShowOriginal();
        }

        _findVisible = true;
        SetFindBarVisible(true);
        Layout();
        UpdateFindStatus();
        _ = NativeMethods.SetFocus(_findEdit);
    }

    internal void ShowFindForTest(string query)
    {
        ShowFind();
        _ = NativeMethods.SetWindowText(_findEdit, query ?? string.Empty);
        _findPosition = 0;
        UpdateFindStatus();
    }

    internal void HideFind()
    {
        if (!_findVisible)
        {
            return;
        }

        _findVisible = false;
        ResetFindComposition();
        CancelFindWork();
        ClearFindHighlights();
        SetFindBarVisible(false);
        Layout();
        if (ActiveEditor is not null)
        {
            _ = NativeMethods.SetFocus(ActiveEditor.Handle);
        }
    }

    internal void GoToLine(int lineNumber, bool focus = true)
    {
        ActiveEditor?.GoToLine(lineNumber, focus);
    }

    internal bool ShowBlame(IReadOnlyList<GitBlameLine> lines, Action<string> openCommit)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(openCommit);
        if (_originalEditor is null || _result.Status != DocumentReadStatus.TextReady)
        {
            return false;
        }

        ShowOriginal();
        _openBlameCommit = openCommit;
        _originalEditor.SetBlame(lines);
        SetBlameToolbarVisible(true, lines.Count);
        return true;
    }

    private void ClearBlame(bool focus = true)
    {
        if (!_showBlame) return;
        _originalEditor?.ClearBlame();
        _openBlameCommit = null;
        SetBlameToolbarVisible(false);
        if (focus && _originalEditor is not null) _ = NativeMethods.SetFocus(_originalEditor.Handle);
    }

    internal void ShowGoToLine()
    {
        ShowGoToLinePrompt();
    }

    internal Task NavigateToAnchorAsync(string anchor)
    {
        if (_markdownPreview is not null)
        {
            return _markdownPreview.NavigateToAnchorAsync(anchor);
        }

        _pendingAnchor = anchor;
        return Task.CompletedTask;
    }

    internal async Task<int> ShowMarkdownPreviewForTestAsync()
    {
        await SetMarkdownModeAsync(DocumentDisplayMode.Preview);
        return MarkdownBrowserProcessId;
    }

    internal async Task<int> ShowMarkdownSplitForTestAsync()
    {
        await SetMarkdownModeAsync(DocumentDisplayMode.Split);
        return MarkdownBrowserProcessId;
    }

    internal int CloseMarkdownPreviewForTest()
    {
        int processId = MarkdownBrowserProcessId;
        ShowOriginal();
        _markdownPreview?.Dispose();
        _markdownPreview = null;
        _markdownPreviewDirty = true;
        return processId;
    }

    public void Dispose()
    {
        ClearToolbarHover(_hoveredToolbarButton);
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReadResultChanged = null;
        CancelImageLoad();
        CancelFindWork();
        CancelMarkdownSplitterDrag();
        _originalEditor?.Dispose();
        _formattedEditor?.Dispose();
        _imageView?.Dispose();
        PreserveImageWorkers();
        _infoView?.Dispose();
        _markdownPreview?.Dispose();
        CancelMarkdownPreviewWork();
        _imageToolTip?.Dispose();
        _modeToolTip?.Dispose();
        _documentToolTip?.Dispose();
        _contextMenu?.Dispose();
        _originalEditor = null;
        _formattedEditor = null;
        _formattedJson = null;
        _imageView = null;
        _infoView = null;
        _markdownPreview = null;
        _markdownPreviewCancellation = null;
        _imageToolTip = null;
        _modeToolTip = null;
        _documentToolTip = null;
        _contextMenu = null;
        _findCountCache = null;
        RemoveFindEditSubclass();
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }
        nint handle = Handle;
        Handle = 0;
        if (handle != 0)
        {
            lock (InstancesGate)
            {
                Instances.Remove(handle);
            }

            if (NativeMethods.IsWindow(handle))
            {
                _ = NativeMethods.DestroyWindow(handle);
            }
        }
    }

    private ScintillaControl? ActiveEditor => _showAlternative && _formattedEditor is not null
        ? _formattedEditor
        : _originalEditor;

    private string ActiveText => _showAlternative && _formattedEditor is not null
        ? _formattedJson?.DisplayText ?? string.Empty
        : _result.Text ?? string.Empty;

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
                Style = NativeMethods.ClassRedrawOnHorizontalChange | NativeMethods.ClassRedrawOnVerticalChange,
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
                throw new Win32Exception(error, UiText.DocumentViewClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeDocumentView? instance;
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
            case NativeMethods.WindowMessageShowWindow:
            case NativeMethods.WindowMessageEnable:
                if (wordParameter == 0)
                {
                    instance.ClearToolbarHover(instance._hoveredToolbarButton);
                    instance.CancelMarkdownSplitterDrag();
                }
                break;
            case NativeMethods.WindowMessageCancelMode:
                instance.CancelMarkdownSplitterDrag();
                break;
            case NativeMethods.WindowMessageKeyDown when wordParameter == NativeMethods.VirtualKeyEscape:
                if (instance.TryCancelMarkdownSplitterDrag()) return 0;
                break;
            case 0x0082:
                instance.Handle = 0;
                lock (InstancesGate) Instances.Remove(window);
                instance.Dispose();
                break;
            case ImageDispatchMessage:
                instance.ApplyImageLoad();
                return 0;
            case FindDispatchMessage:
                instance.DrainFindActions();
                return 0;
            case NativeMethods.WindowMessageSize:
                instance.Layout();
                return 0;
            case NativeMethods.WindowMessageNotify:
                if (instance.HandleNotification(longParameter))
                {
                    return 0;
                }

                break;
            case NativeMethods.WindowMessageSetCursor:
                if (instance.TrySetMarkdownSplitterCursor())
                {
                    return 1;
                }

                break;
            case NativeMethods.WindowMessageLeftButtonDown:
                if (instance.BeginMarkdownSplitterDrag(longParameter))
                {
                    return 0;
                }

                break;
            case NativeMethods.WindowMessageMouseMove:
                if (instance.UpdateMarkdownSplitterDrag(longParameter))
                {
                    return 0;
                }

                break;
            case NativeMethods.WindowMessageLeftButtonUp:
                if (instance.EndMarkdownSplitterDrag(longParameter))
                {
                    return 0;
                }

                break;
            case NativeMethods.WindowMessageCaptureChanged:
                instance._draggingMarkdownSplitter = false;
                instance._markdownSplitterGrabOffset = 0;
                break;
            case NativeMethods.WindowMessageCommand:
                instance.HandleCommand(wordParameter);
                return 0;
            case NativeMethods.WindowMessageControlColorEdit:
            case NativeMethods.WindowMessageControlColorButton:
            case NativeMethods.WindowMessageControlColorStatic:
                return instance.ApplyControlColor(unchecked((nint)wordParameter));
            case NativeMethods.WindowMessageDrawItem:
                return instance.DrawControl(longParameter) ? 1 : 0;
            case NativeMethods.WindowMessageEraseBackground:
                return instance.PaintBackground(unchecked((nint)wordParameter));
            case NativeMethods.WindowMessageSetFocus:
                if (instance.ActiveEditor is not null)
                {
                    _ = NativeMethods.SetFocus(instance.ActiveEditor.Handle);
                }
                else
                {
                    instance._infoView?.FocusPrimaryAction();
                }

                return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private static nint HandleFindEditMessage(
        nint window,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        _ = subclassIdentifier;
        _ = referenceData;
        NativeDocumentView? instance;
        lock (InstancesGate)
        {
            FindEditInstances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        }

        if (window == instance._findOverlay)
        {
            if (message == NativeMethods.WindowMessageLeftButtonDown)
            {
                int x = unchecked((short)NativeMethods.LowWord((nuint)longParameter));
                int y = unchecked((short)NativeMethods.HighWord((nuint)longParameter));
                NativeMethods.Rectangle frame = instance._findEditFrame;
                if (x >= frame.Left && x < frame.Right && y >= frame.Top && y < frame.Bottom)
                {
                    _ = NativeMethods.SetFocus(instance._findEdit);
                    return 0;
                }
            }
            return NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        }

        if (message == 0x010D) instance.BeginFindComposition();
        if (message == 0x010E)
        {
            // 先让原生 Edit 接纳最终文字，再结束组词；过程中的 EN_CHANGE 不能抢先扫描。
            nint result = NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
            instance.CompleteFindComposition(preservePosition: false);
            return result;
        }
        if (message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
        {
            nint result = NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
            if (message == NativeMethods.WindowMessageKillFocus)
                instance.CompleteFindComposition(preservePosition: true);
            _ = NativeMethods.InvalidateRectangle(instance._findOverlay, 0, false);
            return result;
        }
        if (message != NativeMethods.WindowMessageKeyDown || instance._findComposing)
            return NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);

        int key = unchecked((int)wordParameter);
        if (key == NativeMethods.VirtualKeyEnter)
        {
            bool backwards = NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0;
            instance.FindNext(backwards);
            return 0;
        }

        if (key == NativeMethods.VirtualKeyEscape)
        {
            instance.HideFind();
            return 0;
        }

        return NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
    }

    private void AddFindEditSubclass()
    {
        // 浮层仅接收输入框留白处的点击；输入、组词和选择仍由同一个原生 Edit 处理。
        foreach (nint control in new[] { _findEdit, _findOverlay })
        {
            if (!NativeMethods.SetWindowSubclass(control, FindEditProcedure, FindEditSubclassIdentifier, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.DocumentControlCreateFailed);

            lock (InstancesGate) FindEditInstances[control] = this;
        }
    }

    private void RemoveFindEditSubclass()
    {
        foreach (nint control in new[] { _findEdit, _findOverlay })
        {
            if (control == 0) continue;
            _ = NativeMethods.RemoveWindowSubclass(control, FindEditProcedure, FindEditSubclassIdentifier);
            lock (InstancesGate) FindEditInstances.Remove(control);
        }
    }

    private nint PaintBackground(nint deviceContext)
    {
        if (deviceContext == 0 || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle rectangle))
        {
            return 0;
        }

        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        nint brush = NativeMethods.CreateSolidBrush(palette.Panel);
        if (brush != 0)
        {
            _ = NativeMethods.FillRectangle(deviceContext, ref rectangle, brush);
            _ = NativeMethods.DeleteObject(brush);
        }

        NativeMethods.Rectangle line = rectangle;
        line.Top = Math.Min(line.Bottom, ToolbarHeight - 1);
        line.Bottom = Math.Min(line.Bottom, ToolbarHeight);
        nint lineBrush = NativeMethods.CreateSolidBrush(palette.Border);
        if (lineBrush != 0)
        {
            _ = NativeMethods.FillRectangle(deviceContext, ref line, lineBrush);
            _ = NativeMethods.DeleteObject(lineBrush);
        }

        if (IsMarkdownSplitMode() && rectangle.Right > rectangle.Left)
        {
            int contentTop = GetContentTop();
            int sourceWidth = GetMarkdownSplitSourceWidth(rectangle.Right - rectangle.Left);
            int splitterCenter = sourceWidth + MarkdownSplitterWidth / 2;
            NativeMethods.Rectangle splitter = new()
            {
                Left = Math.Max(rectangle.Left, splitterCenter),
                Top = Math.Min(rectangle.Bottom, contentTop),
                Right = Math.Min(rectangle.Right, splitterCenter + Math.Max(1, NativeTheme.Scale(1))),
                Bottom = rectangle.Bottom,
            };
            if (splitter.Right > splitter.Left)
            {
                nint splitterBrush = NativeMethods.CreateSolidBrush(palette.Border);
                if (splitterBrush != 0)
                {
                    _ = NativeMethods.FillRectangle(deviceContext, ref splitter, splitterBrush);
                    _ = NativeMethods.DeleteObject(splitterBrush);
                }
            }
        }

        if (_modeSegmentVisible)
        {
            DrawModeSegmentBackground(deviceContext, _modeSegmentRectangle);
        }

        return 1;
    }

    private nint ApplyControlColor(nint deviceContext)
    {
        if (deviceContext == 0 || _controlBrush == 0)
        {
            return 0;
        }

        NativeThemePalette palette = NativeTheme.Palette(NativeTheme.IsDark(_settings.Theme));
        _ = NativeMethods.SetBackgroundColor(deviceContext, palette.Panel);
        _ = NativeMethods.SetTextColor(deviceContext, palette.Text);
        return _controlBrush;
    }

    private bool DrawControl(nint parameter)
    {
        if (parameter == 0)
        {
            return false;
        }

        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        // OwnerDraw 的悬停标志并非总由系统提供，使用当前文档的真实鼠标消息状态。
        item.ItemState &= ~NativeMethods.OwnerDrawHotLight;
        if (item.Control == _hoveredToolbarButton) item.ItemState |= NativeMethods.OwnerDrawHotLight;
        if (item.ControlIdentifier == TargetPathIdentifier)
        {
            DrawTargetNotice(item);
            return true;
        }
        if (item.ControlIdentifier == CommandJsonError)
        {
            DrawJsonError(item);
            return true;
        }
        if (item.ControlIdentifier == FindOverlayIdentifier)
        {
            DrawFindOverlayBackground(item);
            return true;
        }

        if (item.ControlIdentifier is BreadcrumbIdentifier
            or PreviewStatusIdentifier
            or ImageSizeIdentifier
            or ImageZoomIdentifier
            or FindStatusIdentifier
            or BlameCountIdentifier)
        {
            bool dark = NativeTheme.IsDark(_settings.Theme);
            NativeThemePalette palette = NativeTheme.Palette(dark);
            nint brush = NativeMethods.CreateSolidBrush(palette.Panel);
            if (brush != 0)
            {
                _ = NativeMethods.FillRectangle(item.DeviceContext, ref item.ItemRectangle, brush);
                _ = NativeMethods.DeleteObject(brush);
            }

            NativeMethods.Rectangle textRectangle = item.ItemRectangle;
            nint font = item.ControlIdentifier == ImageZoomIdentifier
                ? NativeTheme.UiMediumFont
                : NativeTheme.UiFont;
            nint previousFont = NativeMethods.SelectObject(item.DeviceContext, font);
            _ = NativeMethods.SetBackgroundMode(item.DeviceContext, NativeMethods.BackgroundModeTransparent);
            _ = NativeMethods.SetTextColor(
                item.DeviceContext,
                item.ControlIdentifier == ImageZoomIdentifier ? palette.Text : palette.Muted);
            string label = NativeMethods.GetWindowTextValue(item.Control);
            uint format = NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextNoPrefix
                | NativeMethods.DrawTextEndEllipsis;
            if (item.ControlIdentifier is PreviewStatusIdentifier
                or ImageSizeIdentifier
                or ImageZoomIdentifier
                or FindStatusIdentifier)
            {
                format |= NativeMethods.DrawTextCenter;
            }
            if (item.ControlIdentifier == BlameCountIdentifier) format |= NativeMethods.DrawTextRight;
            _ = NativeMethods.DrawText(item.DeviceContext, label, label.Length, ref textRectangle, format);
            if (previousFont != 0)
            {
                _ = NativeMethods.SelectObject(item.DeviceContext, previousFont);
            }

            return true;
        }

        if (item.ControlIdentifier is CommandImageZoomOut or CommandImageZoomIn or CommandImageFitToArea)
        {
            DrawDocumentActionBackground(item);
            DrawImageToolbarIcon(item);
            return true;
        }

        if (item.ControlIdentifier is CommandOriginal or CommandAlternative or CommandSplit)
        {
            bool modeSelected = IsModeButtonSelected(unchecked((int)item.ControlIdentifier));
            DrawModeToolbarButton(item, modeSelected);
            return true;
        }

        if (item.ControlIdentifier is CommandFind
            or CommandGoToLine
            or CommandWordWrap
            or CommandWhitespace
            or CommandMore)
        {
            bool toolbarSelected = item.ControlIdentifier switch
            {
                CommandWordWrap => IsChecked(_wordWrapButton),
                CommandWhitespace => IsChecked(_whitespaceButton),
                _ => false,
            };
            DrawDocumentActionBackground(item, toolbarSelected);
            DrawDocumentToolbarIcon(item);
            return true;
        }

        if (item.ControlIdentifier is CommandMatchCase
            or CommandWholeWord
            or CommandRegularExpression
            or CommandFindPrevious
            or CommandFindNext
            or CommandCloseFind
            or CommandCloseBlame)
        {
            bool findSelected = item.ControlIdentifier switch
            {
                CommandMatchCase => IsChecked(_matchCaseButton),
                CommandWholeWord => IsChecked(_wholeWordButton),
                CommandRegularExpression => IsChecked(_regularExpressionButton),
                _ => false,
            };
            DrawDocumentActionBackground(item, findSelected);
            DrawFindButtonIcon(item);
            return true;
        }

        bool selected = item.ControlIdentifier switch
        {
            CommandOriginal => !_showAlternative,
            CommandAlternative => _showAlternative && _displayMode != DocumentDisplayMode.Split,
            CommandSplit => _displayMode == DocumentDisplayMode.Split,
            CommandWordWrap => IsChecked(_wordWrapButton),
            CommandWhitespace => IsChecked(_whitespaceButton),
            CommandMatchCase => IsChecked(_matchCaseButton),
            CommandWholeWord => IsChecked(_wholeWordButton),
            CommandRegularExpression => IsChecked(_regularExpressionButton),
            _ => false,
        };
        return NativeTheme.DrawFlatButton(parameter, NativeTheme.IsDark(_settings.Theme), selected: selected);
    }

    private void DrawFindOverlayBackground(NativeMethods.DrawItem item)
    {
        NativeThemePalette palette = NativeTheme.Palette(NativeTheme.IsDark(_settings.Theme));
        NativeMethods.Rectangle rectangle = item.ItemRectangle;
        int inset = Math.Max(1, NativeTheme.Scale(1));
        nint brush = NativeMethods.CreateSolidBrush(palette.Panel);
        try { _ = NativeMethods.FillRectangle(item.DeviceContext, ref rectangle, brush); }
        finally { if (brush != 0) _ = NativeMethods.DeleteObject(brush); }
        rectangle.Top = Math.Max(rectangle.Top, rectangle.Bottom - inset);
        brush = NativeMethods.CreateSolidBrush(palette.Border);
        try { _ = NativeMethods.FillRectangle(item.DeviceContext, ref rectangle, brush); }
        finally { if (brush != 0) _ = NativeMethods.DeleteObject(brush); }

        NativeTheme.FillRounded(item.DeviceContext, _findEditFrame,
            NativeMethods.GetFocus() == _findEdit ? palette.Accent : palette.Border, NativeTheme.Scale(10));
        NativeMethods.Rectangle input = _findEditFrame;
        input.Left += inset;
        input.Top += inset;
        input.Right -= inset;
        input.Bottom -= inset;
        NativeTheme.FillRounded(item.DeviceContext, input, palette.Panel, NativeTheme.Scale(8));
    }

    private void DrawModeSegmentBackground(
        nint deviceContext,
        NativeMethods.Rectangle segmentRectangle)
    {
        NativeThemePalette palette = NativeTheme.Palette(NativeTheme.IsDark(_settings.Theme));
        nint brush = NativeMethods.CreateSolidBrush(palette.Panel);
        if (brush == 0) return;
        try
        {
            _ = NativeMethods.FillRectangle(deviceContext, ref segmentRectangle, brush);
        }
        finally
        {
            _ = NativeMethods.DeleteObject(brush);
        }
    }

    private void DrawModeToolbarButton(NativeMethods.DrawItem item, bool selected)
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        bool disabled = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0;
        bool pressed = !disabled && (item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0;
        (uint background, uint color) = ResolveModeToolbarButtonColors(
            dark,
            selected,
            disabled,
            pressed);
        // 无选中态的按钮与工具栏共底，仅当前模式绘制内缩的圆角背景。
        nint brush = NativeMethods.CreateSolidBrush(palette.Panel);
        if (brush != 0)
        {
            _ = NativeMethods.FillRectangle(item.DeviceContext, ref item.ItemRectangle, brush);
            _ = NativeMethods.DeleteObject(brush);
        }
        if (selected || pressed)
        {
            NativeMethods.Rectangle highlight = item.ItemRectangle;
            highlight.Left += NativeTheme.Scale(2);
            highlight.Top += NativeTheme.Scale(2);
            highlight.Right -= NativeTheme.Scale(2);
            highlight.Bottom -= NativeTheme.Scale(2);
            _ = NativeGdiPlusDrawing.FillRoundedRectangle(
                item.DeviceContext, highlight, background, NativeTheme.Scale(10));
        }

        int centerX = (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2;
        int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
        _ = NativeTheme.DrawDocumentModeIcon(
            item.DeviceContext,
            centerX,
            centerY,
            ResolveDocumentModeIcon(_result.Classification.Kind, item.ControlIdentifier),
            color);

        if (!disabled) NativeTheme.DrawToolbarFocus(item, palette);
    }

    private static (uint Background, uint Icon) ResolveModeToolbarButtonColors(
        bool dark,
        bool selected,
        bool disabled,
        bool pressed)
    {
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint background = selected
            ? NativeTheme.DocumentModeSelectedBackground(dark)
            : pressed ? palette.Hover : palette.Panel;
        uint icon = disabled ? palette.Faint : NativeTheme.DocumentModeIconColor(dark);
        return (background, icon);
    }

    private void DrawImageToolbarIcon(NativeMethods.DrawItem item)
    {
        NativeTheme.DrawImageActionIcon(item.DeviceContext,
            (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2,
            (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2,
            item.ControlIdentifier == CommandImageFitToArea,
            item.ControlIdentifier == CommandImageZoomIn,
            ResolveIconColor(item));
    }

    private void DrawDocumentToolbarIcon(NativeMethods.DrawItem item)
    {
        uint color = ResolveIconColor(item);
        int centerX = (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2;
        int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
        nint pen = NativeMethods.CreatePen(
            NativeMethods.PenStyleSolid,
            Math.Max(1, NativeTheme.Scale(1)),
            color);
        if (pen == 0)
        {
            return;
        }

        nint previousPen = NativeMethods.SelectObject(item.DeviceContext, pen);
        nint previousBrush = NativeMethods.SelectObject(
            item.DeviceContext,
            NativeMethods.GetStockObject(NativeMethods.NullBrush));
        int unit = NativeTheme.Scale(1);
        switch (item.ControlIdentifier)
        {
            case CommandMore:
                _ = NativeTheme.DrawMoreIcon(item.DeviceContext, centerX, centerY, color);
                break;
            case CommandFind:
                int radius = NativeTheme.Scale(5);
                _ = NativeMethods.DrawEllipse(
                    item.DeviceContext,
                    centerX - radius - unit,
                    centerY - radius - unit,
                    centerX + radius - unit,
                    centerY + radius - unit);
                _ = NativeMethods.MoveTo(
                    item.DeviceContext,
                    centerX + NativeTheme.Scale(3),
                    centerY + NativeTheme.Scale(3),
                    0);
                _ = NativeMethods.LineTo(
                    item.DeviceContext,
                    centerX + NativeTheme.Scale(7),
                    centerY + NativeTheme.Scale(7));
                break;
            case CommandGoToLine:
                _ = NativeMethods.MoveTo(
                    item.DeviceContext,
                    centerX - NativeTheme.Scale(6),
                    centerY - NativeTheme.Scale(6),
                    0);
                _ = NativeMethods.LineTo(
                    item.DeviceContext,
                    centerX - NativeTheme.Scale(6),
                    centerY + NativeTheme.Scale(3));
                _ = NativeMethods.LineTo(
                    item.DeviceContext,
                    centerX + NativeTheme.Scale(5),
                    centerY + NativeTheme.Scale(3));
                _ = NativeMethods.MoveTo(
                    item.DeviceContext,
                    centerX + NativeTheme.Scale(1),
                    centerY - NativeTheme.Scale(1),
                    0);
                _ = NativeMethods.LineTo(
                    item.DeviceContext,
                    centerX + NativeTheme.Scale(5),
                    centerY + NativeTheme.Scale(3));
                _ = NativeMethods.LineTo(
                    item.DeviceContext,
                    centerX + NativeTheme.Scale(1),
                    centerY + NativeTheme.Scale(7));
                break;
            case CommandWordWrap:
                for (int index = -1; index <= 1; index++)
                {
                    int y = centerY + index * NativeTheme.Scale(5);
                    int right = index == 0 ? centerX + NativeTheme.Scale(5) : centerX + NativeTheme.Scale(7);
                    _ = NativeMethods.MoveTo(item.DeviceContext, centerX - NativeTheme.Scale(7), y, 0);
                    _ = NativeMethods.LineTo(item.DeviceContext, right, y);
                }

                _ = NativeMethods.LineTo(
                    item.DeviceContext,
                    centerX + NativeTheme.Scale(5),
                    centerY + NativeTheme.Scale(4));
                _ = NativeMethods.MoveTo(
                    item.DeviceContext,
                    centerX + NativeTheme.Scale(1),
                    centerY,
                    0);
                _ = NativeMethods.LineTo(
                    item.DeviceContext,
                    centerX + NativeTheme.Scale(5),
                    centerY + NativeTheme.Scale(4));
                _ = NativeMethods.LineTo(
                    item.DeviceContext,
                    centerX + NativeTheme.Scale(1),
                    centerY + NativeTheme.Scale(8));
                break;
            case CommandWhitespace:
                int top = centerY - NativeTheme.Scale(7);
                int bottom = centerY + NativeTheme.Scale(7);
                _ = NativeMethods.DrawEllipse(
                    item.DeviceContext,
                    centerX - NativeTheme.Scale(6),
                    top,
                    centerX + NativeTheme.Scale(2),
                    centerY + NativeTheme.Scale(1));
                _ = NativeMethods.MoveTo(item.DeviceContext, centerX, top, 0);
                _ = NativeMethods.LineTo(item.DeviceContext, centerX, bottom);
                _ = NativeMethods.MoveTo(item.DeviceContext, centerX + NativeTheme.Scale(4), top, 0);
                _ = NativeMethods.LineTo(item.DeviceContext, centerX + NativeTheme.Scale(4), bottom);
                break;
        }

        if (previousBrush != 0)
        {
            _ = NativeMethods.SelectObject(item.DeviceContext, previousBrush);
        }

        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(item.DeviceContext, previousPen);
        }

        _ = NativeMethods.DeleteObject(pen);
    }

    private void DrawFindButtonIcon(NativeMethods.DrawItem item)
    {
        uint color = ResolveIconColor(item);
        int centerX = (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2;
        int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
        if (item.ControlIdentifier is CommandMatchCase or CommandWholeWord or CommandRegularExpression)
        {
            NativeFindOptionIcon icon = item.ControlIdentifier switch
            {
                CommandMatchCase => NativeFindOptionIcon.MatchCase,
                CommandWholeWord => NativeFindOptionIcon.WholeWord,
                _ => NativeFindOptionIcon.RegularExpression,
            };
            _ = NativeTheme.DrawFindOptionIcon(item.DeviceContext, item.ItemRectangle, icon, color);
            return;
        }

        nint pen = NativeMethods.CreatePen(
            NativeMethods.PenStyleSolid,
            Math.Max(1, NativeTheme.Scale(1)),
            color);
        if (pen == 0)
        {
            return;
        }

        nint previousPen = NativeMethods.SelectObject(item.DeviceContext, pen);
        switch (item.ControlIdentifier)
        {
            case CommandFindPrevious:
                DrawChevron(item.DeviceContext, centerX, centerY, up: true);
                break;
            case CommandFindNext:
                DrawChevron(item.DeviceContext, centerX, centerY, up: false);
                break;
            case CommandCloseFind:
            case CommandCloseBlame:
                int extent = NativeTheme.Scale(5);
                _ = NativeMethods.MoveTo(item.DeviceContext, centerX - extent, centerY - extent, 0);
                _ = NativeMethods.LineTo(item.DeviceContext, centerX + extent, centerY + extent);
                _ = NativeMethods.MoveTo(item.DeviceContext, centerX + extent, centerY - extent, 0);
                _ = NativeMethods.LineTo(item.DeviceContext, centerX - extent, centerY + extent);
                break;
        }

        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(item.DeviceContext, previousPen);
        }

        _ = NativeMethods.DeleteObject(pen);
    }

    private static void DrawChevron(
        nint deviceContext,
        int centerX,
        int centerY,
        bool up)
    {
        int horizontal = NativeTheme.Scale(5);
        int vertical = NativeTheme.Scale(3);
        int baseY = centerY + (up ? vertical : -vertical);
        int apexY = centerY + (up ? -vertical : vertical);
        _ = NativeMethods.MoveTo(
            deviceContext,
            centerX - horizontal,
            baseY,
            0);
        _ = NativeMethods.LineTo(deviceContext, centerX, apexY);
        _ = NativeMethods.LineTo(
            deviceContext,
            centerX + horizontal,
            baseY);
    }

    private uint ResolveIconColor(NativeMethods.DrawItem item)
    {
        NativeThemePalette palette = NativeTheme.Palette(NativeTheme.IsDark(_settings.Theme));
        if ((item.ItemState & NativeMethods.OwnerDrawDisabled) != 0)
        {
            return palette.Faint;
        }

        return (item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0
            ? palette.Text : palette.Muted;
    }

    private static void DrawCorner(nint deviceContext, int x, int y, int horizontal, int vertical)
    {
        _ = NativeMethods.MoveTo(deviceContext, x + horizontal, y, 0);
        _ = NativeMethods.LineTo(deviceContext, x, y);
        _ = NativeMethods.LineTo(deviceContext, x, y + vertical);
    }

    private void ToggleChecked(nint button)
    {
        // OwnerDraw 按钮不保存 BM_SETCHECK 状态，绘制与命令统一读取文档自己的选项。
        if (button == _wordWrapButton) _wordWrap = !_wordWrap;
        else if (button == _whitespaceButton) _showWhitespace = !_showWhitespace;
        else if (button == _matchCaseButton) _matchCase = !_matchCase;
        else if (button == _wholeWordButton) _wholeWord = !_wholeWord;
        else if (button == _regularExpressionButton) _regularExpression = !_regularExpression;
        _ = NativeMethods.InvalidateRectangle(button, 0, true);
    }

    private void InvalidateToolbar()
    {
        foreach (nint control in _toolbarControls.Concat(_imageToolbarControls))
        {
            _ = NativeMethods.InvalidateRectangle(control, 0, true);
        }

        _ = NativeMethods.InvalidateRectangle(Handle, 0, true);
    }

    private void CreateToolbar()
    {
        _breadcrumbLabel = CreateChild(
            NativeMethods.StaticClass,
            string.Empty,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.StaticOwnerDraw,
            BreadcrumbIdentifier);
        _originalButton = CreateButton(UiText.Original, CommandOriginal);
        _splitButton = CreateButton(UiText.Split, CommandSplit);
        _alternativeButton = CreateButton(UiText.Preview, CommandAlternative);
        _findButton = CreateButton(string.Empty, CommandFind);
        _goToButton = CreateButton(string.Empty, CommandGoToLine);
        _wordWrapButton = CreateCheckbox(string.Empty, CommandWordWrap);
        _whitespaceButton = CreateCheckbox(string.Empty, CommandWhitespace);
        _moreButton = CreateButton(string.Empty, CommandMore);
    }

    private void CreateImageToolbar()
    {
        _imageSizeLabel = CreateChild(
            NativeMethods.StaticClass,
            string.Empty,
            NativeMethods.WindowStyleChild | NativeMethods.StaticOwnerDraw,
            ImageSizeIdentifier);
        _imageZoomOutButton = CreateButton(string.Empty, CommandImageZoomOut, false);
        _imageZoomLabel = CreateChild(
            NativeMethods.StaticClass,
            string.Empty,
            NativeMethods.WindowStyleChild | NativeMethods.StaticOwnerDraw,
            ImageZoomIdentifier);
        _imageZoomInButton = CreateButton(string.Empty, CommandImageZoomIn, false);
        _imageFitButton = CreateButton(string.Empty, CommandImageFitToArea, false);
        _imageToolbarControls.AddRange(
            [_imageSizeLabel, _imageZoomOutButton, _imageZoomLabel, _imageZoomInButton, _imageFitButton]);
        SetImageToolbarVisible(false);
    }

    private void CreateToolTips()
    {
        _imageToolTip = new(Handle);
        _imageToolTip.Add(_imageZoomOutButton, UiText.ImageZoomOut);
        _imageToolTip.Add(_imageZoomInButton, UiText.ImageZoomIn);
        _imageToolTip.Add(_imageFitButton, UiText.ImageFitToArea);
        _imageToolTip.Add(_imageSizeLabel, "图片像素尺寸");
        _imageToolTip.Add(_imageZoomLabel, "100%");
        _documentToolTip = new(Handle);
        _documentToolTip.Add(_breadcrumbLabel, _result.RequestedPath);
        _documentToolTip.Add(_wordWrapButton, UiText.WordWrap);
        _documentToolTip.Add(_whitespaceButton, UiText.ShowWhitespace);
        _documentToolTip.Add(_findButton, UiText.Find);
        _documentToolTip.Add(_goToButton, UiText.GoToLine);
        _documentToolTip.Add(_moreButton, UiText.MoreActions);
        _documentToolTip.Add(_matchCaseButton, UiText.MatchCase);
        _documentToolTip.Add(_wholeWordButton, UiText.MatchWholeWord);
        _documentToolTip.Add(_regularExpressionButton, UiText.RegularExpression);
        _documentToolTip.Add(_findPreviousButton, UiText.Previous);
        _documentToolTip.Add(_findNextButton, UiText.Next);
        _documentToolTip.Add(_findCloseButton, UiText.Close);
        _documentToolTip.Add(_blameCloseButton, UiText.CloseBlame);
        _documentToolTip.Add(_blameCountLabel, UiText.Blame);
        RefreshModeToolTips();
    }

    private void RefreshModeToolTips()
    {
        _modeToolTip?.Dispose();
        _modeToolTip = new(Handle);
        _modeToolTip.Add(_originalButton, UiText.Original);
        _modeToolTip.Add(
            _alternativeButton,
            _result.Classification.Kind == DocumentKind.Json ? UiText.Formatted : UiText.Preview);
        _modeToolTip.Add(_splitButton, UiText.Split);
    }

    private void CreateFindBar()
    {
        _findOverlay = CreateChild(
            NativeMethods.StaticClass,
            string.Empty,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleClipSiblings
                | NativeMethods.StaticNotify
                | NativeMethods.StaticOwnerDraw,
            FindOverlayIdentifier);
        _findEdit = CreateChild(
            NativeMethods.EditClass,
            string.Empty,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleClipSiblings
                | NativeMethods.WindowStyleTabStop
                | NativeMethods.EditAutoHorizontalScroll,
            FindEditIdentifier);
        _ = NativeMethods.SendMessage(_findEdit, NativeMethods.EditSetCueBanner, 1, UiText.Find);
        AddFindEditSubclass();
        _matchCaseButton = CreateCheckbox(string.Empty, CommandMatchCase, false);
        _wholeWordButton = CreateCheckbox(string.Empty, CommandWholeWord, false);
        _regularExpressionButton = CreateCheckbox(string.Empty, CommandRegularExpression, false);
        _findPreviousButton = CreateButton(string.Empty, CommandFindPrevious, false);
        _findNextButton = CreateButton(string.Empty, CommandFindNext, false);
        _findCloseButton = CreateButton(string.Empty, CommandCloseFind, false);
        _findStatus = CreateChild(
            NativeMethods.StaticClass,
            string.Empty,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleClipSiblings
                | NativeMethods.StaticOwnerDraw,
            FindStatusIdentifier);
        SetFindBarVisible(false);
    }

    private void CreateBlameToolbar()
    {
        _blameCountLabel = CreateChild(
            NativeMethods.StaticClass,
            string.Empty,
            NativeMethods.WindowStyleChild | NativeMethods.StaticOwnerDraw,
            BlameCountIdentifier);
        _blameCloseButton = CreateButton(string.Empty, CommandCloseBlame, false);
        _ = NativeMethods.ShowWindow(_blameCountLabel, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_blameCloseButton, NativeMethods.ShowHide);
    }

    private nint CreateButton(string text, int command, bool toolbar = true)
    {
        nint handle = CreateChild(
            NativeMethods.ButtonClass,
            text,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleClipSiblings
                | NativeMethods.WindowStyleTabStop
                | NativeMethods.ButtonOwnerDraw,
            command);
        if (toolbar)
        {
            _toolbarControls.Add(handle);
        }

        return handle;
    }

    private nint CreateCheckbox(string text, int command, bool toolbar = true)
    {
        nint handle = CreateChild(
            NativeMethods.ButtonClass,
            text,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleClipSiblings
                | NativeMethods.WindowStyleTabStop
                | NativeMethods.ButtonOwnerDraw,
            command);
        if (toolbar)
        {
            _toolbarControls.Add(handle);
        }

        return handle;
    }

    private nint CreateChild(string className, string text, uint style, int identifier)
    {
        nint child = NativeMethods.CreateWindow(
            0,
            className,
            text,
            style,
            0,
            0,
            0,
            0,
            Handle,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (child == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.DocumentControlCreateFailed);
        }

        _ = NativeMethods.SendMessage(child, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        if (className == NativeMethods.ButtonClass && identifier != CommandJsonError
            && !NativeMethods.SetWindowSubclass(child, ToolbarButtonProcedure, ToolbarButtonSubclassIdentifier, (nuint)Handle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.DocumentControlCreateFailed);
        return child;
    }

    private void ApplyResult()
    {
        CancelFindWork();
        ReadStatusText = _result.Message.Length == 0 ? _result.Classification.TypeName : _result.Message;
        DisposeContentControls();
        _openBlameCommit = null;
        string relativePath = System.IO.Path.GetRelativePath(_workspaceRoot, _result.RequestedPath)
            .Replace(System.IO.Path.DirectorySeparatorChar.ToString(), "  ›  ", StringComparison.Ordinal);
        _ = NativeMethods.SetWindowText(
            _breadcrumbLabel,
            $"{System.IO.Path.GetFileName(_workspaceRoot)}  ›  {relativePath}    只读");
        _documentToolTip?.Update(_breadcrumbLabel, _result.RequestedPath);
        _showAlternative = false;
        _ = NativeMethods.EnableWindow(_alternativeButton, true);
        _markdownPreviewError = null;
        SetBlameToolbarVisible(false);
        _findCountCache = null;
        _findPosition = 0;
        _lastFindMatch = null;
        RefreshModeToolTips();
        bool textReady = _result.Status == DocumentReadStatus.TextReady;
        if (!textReady)
        {
            _findVisible = false;
            SetFindBarVisible(false);
        }

        if (!_result.RequestedPath.Equals(_result.ResolvedPath, StringComparison.OrdinalIgnoreCase))
        {
            _targetLabel = CreateChild(
                NativeMethods.StaticClass,
                UiText.SymbolicLinkTarget(_result.ResolvedPath),
                NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.StaticOwnerDraw,
                TargetPathIdentifier);
        }

        foreach (nint control in _toolbarControls)
        {
            _ = NativeMethods.ShowWindow(control, textReady ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        }
        bool showModeSegment = textReady
            && _result.Classification.Kind is DocumentKind.Json or DocumentKind.Markdown;
        _modeSegmentVisible = showModeSegment;

        SetImageToolbarVisible(false);
        _ = NativeMethods.ShowWindow(_breadcrumbLabel, NativeMethods.ShowNormal);

        bool showPlainTextToolbar = textReady && _result.Classification.Kind == DocumentKind.Text;
        foreach (nint control in new[] { _wordWrapButton, _whitespaceButton, _findButton, _goToButton })
        {
            _ = NativeMethods.ShowWindow(
                control,
                showPlainTextToolbar ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        }
        _ = NativeMethods.ShowWindow(
            _moreButton,
            textReady && !showPlainTextToolbar ? NativeMethods.ShowNormal : NativeMethods.ShowHide);

        if (textReady)
        {
            CreateTextContent();
            return;
        }

        if (_result.Status == DocumentReadStatus.ImageReady)
        {
            _imageView = new(Handle);
            _imageView.ViewChanged += UpdateImageToolbarState;
            _imageView.ApplyAppearance(NativeTheme.IsDark(_settings.Theme));
            _ = NativeMethods.SetWindowText(_imageSizeLabel, string.Empty);
            SetImageToolbarVisible(true);
            _ = NativeMethods.ShowWindow(_breadcrumbLabel, NativeMethods.ShowHide);
            UpdateImageToolbarState();
            BeginImageLoad();
            return;
        }

        ShowSummary();
    }

    private bool HandleNotification(nint notificationPointer)
    {
        if (_openBlameCommit is null
            || _originalEditor?.TryGetBlameCommit(notificationPointer, out string? commitHash) != true
            || string.IsNullOrWhiteSpace(commitHash))
        {
            return false;
        }

        _openBlameCommit(commitHash);
        return true;
    }

    private void CreateTextContent()
    {
        _originalEditor = new(Handle, 100);
        _originalEditor.SetTextContent(_result.Text ?? string.Empty);
        _originalEditor.SetWordWrap(_wordWrap);
        _originalEditor.SetWhitespaceVisible(_showWhitespace);
        if (_result.Classification.Kind == DocumentKind.Json)
        {
            JsonDisplayResult formatted = JsonDisplayFormatter.Format(_result.Text ?? string.Empty);
            _formattedJson = formatted;
            _formattedEditor = new(Handle, 101);
            _formattedEditor.SetTextContent(formatted.DisplayText);
            _showAlternative = formatted.IsValid;
            _originalEditor.SetVisible(!formatted.IsValid);
            _formattedEditor.SetVisible(formatted.IsValid);
            _ = NativeMethods.SetWindowText(_alternativeButton, UiText.Formatted);
            _ = NativeMethods.ShowWindow(_alternativeButton, NativeMethods.ShowNormal);
            _ = NativeMethods.ShowWindow(_splitButton, NativeMethods.ShowHide);
            _modeSegmentVisible = true;
            if (!formatted.IsValid)
            {
                ReadStatusText = UiText.JsonError(formatted.ErrorLine, formatted.ErrorColumn);
                _setStatus(ReadStatusText);
            }
            UpdateJsonErrorButton(formatted);
        }
        else if (_result.Classification.Kind == DocumentKind.Markdown)
        {
            _ = NativeMethods.SetWindowText(_alternativeButton, UiText.Preview);
            _ = NativeMethods.ShowWindow(_alternativeButton, NativeMethods.ShowNormal);
            _ = NativeMethods.ShowWindow(_splitButton, NativeMethods.ShowNormal);
            _modeSegmentVisible = true;
            _displayMode = DocumentDisplayMode.Preview;
        }
        else
        {
            _ = NativeMethods.ShowWindow(_originalButton, NativeMethods.ShowHide);
            _ = NativeMethods.ShowWindow(_alternativeButton, NativeMethods.ShowHide);
            _ = NativeMethods.ShowWindow(_splitButton, NativeMethods.ShowHide);
            _modeSegmentVisible = false;
        }

        UpdateFindStatus();
    }

    private void ShowSummary(string? overrideMessage = null)
    {
        string size = FormatFileSize(_result.FileSize);
        string message = string.IsNullOrWhiteSpace(overrideMessage) ? _result.Message : overrideMessage;
        string displayType = GetSummaryDisplayType(_result.Classification.Kind, _result.Classification.TypeName);
        if (_result.Classification.Kind is DocumentKind.Gif or DocumentKind.WebP)
        {
            message = UiText.UnsupportedImageFormat;
        }

        _ = NativeMethods.ShowWindow(_breadcrumbLabel, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_targetLabel, NativeMethods.ShowHide);
        _infoView = new(
            Handle,
            UiText.DocumentPreviewUnavailable,
            UiText.DocumentMetadata(System.IO.Path.GetFileName(_result.RequestedPath), displayType, size),
            _result.ResolvedPath,
            message,
            _result.CanOpenExternally,
            () => ShowLaunchResult(ExternalProgramLauncher.OpenWithDefaultApplication(_result.ResolvedPath)));
    }

    private void DisposeContentControls()
    {
        CancelImageLoad();
        _originalEditor?.Dispose();
        _formattedEditor?.Dispose();
        _imageView?.Dispose();
        PreserveImageWorkers();
        _infoView?.Dispose();
        CancelMarkdownPreviewWork();
        _markdownPreview?.Dispose();
        _originalEditor = null;
        _formattedEditor = null;
        _formattedJson = null;
        _imageView = null;
        _infoView = null;
        _markdownPreview = null;
        _markdownPreviewCancellation = null;
        _markdownPreviewDirty = true;
        _markdownPreviewError = null;
        SetBlameToolbarVisible(false);
        SetImageToolbarVisible(false);
        DestroyChild(ref _targetLabel);
        DestroyChild(ref _jsonErrorButton);
        DestroyChild(ref _previewErrorLabel);
        DestroyChild(ref _previewStatusLabel);
    }

    private static void DestroyChild(ref nint child)
    {
        nint handle = child;
        child = 0;
        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
    }

    private void HandleCommand(nuint wordParameter)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (command == FindEditIdentifier && notification == EditNotificationChanged)
        {
            HandleFindInputChanged();
            return;
        }

        switch (command)
        {
            case CommandJsonError:
                if (_formattedJson is { IsValid: false, ErrorLine: { } line } && _originalEditor is not null)
                {
                    _originalEditor.GoToLine((int)line);
                }
                break;
            case CommandOriginal:
                ShowOriginal();
                break;
            case CommandAlternative:
                ShowAlternative();
                break;
            case CommandSplit:
                _ = SetMarkdownModeAsync(DocumentDisplayMode.Split);
                break;
            case CommandFind:
                ShowFind();
                break;
            case CommandGoToLine:
                ShowGoToLinePrompt();
                break;
            case CommandWordWrap:
                ToggleChecked(_wordWrapButton);
                _originalEditor?.SetWordWrap(_wordWrap);
                _formattedEditor?.SetWordWrap(_wordWrap);
                break;
            case CommandWhitespace:
                ToggleChecked(_whitespaceButton);
                _originalEditor?.SetWhitespaceVisible(_showWhitespace);
                _formattedEditor?.SetWhitespaceVisible(_showWhitespace);
                break;
            case CommandMatchCase:
                ToggleChecked(_matchCaseButton);
                _findPosition = 0;
                _lastFindMatch = null;
                UpdateFindStatus();
                break;
            case CommandWholeWord:
                ToggleChecked(_wholeWordButton);
                _findPosition = 0;
                _lastFindMatch = null;
                UpdateFindStatus();
                break;
            case CommandRegularExpression:
                ToggleChecked(_regularExpressionButton);
                _findPosition = 0;
                _lastFindMatch = null;
                UpdateFindStatus();
                break;
            case CommandMore:
                ShowDocumentMenu();
                break;
            case CommandImageZoomOut:
                _imageView?.ZoomOut();
                UpdateImageToolbarState();
                break;
            case CommandImageZoomIn:
                _imageView?.ZoomIn();
                UpdateImageToolbarState();
                break;
            case CommandImageFitToArea:
                _imageView?.FitToArea();
                UpdateImageToolbarState();
                break;
            case CommandFindPrevious:
                FindNext(true);
                break;
            case CommandFindNext:
                FindNext(false);
                break;
            case CommandCloseFind:
                HideFind();
                break;
            case CommandCloseBlame:
                ClearBlame();
                break;
        }
    }

    private void ShowDocumentMenu()
    {
        if (!NativeMethods.GetWindowRectangle(_moreButton, out NativeMethods.Rectangle button))
        {
            return;
        }
        _contextMenu?.Dispose();
        _contextMenu = NativeContextMenu.Show(
            Handle,
            button.Left,
            button.Bottom,
            [
                new(UiText.Find, NativeContextMenuIcon.Search, () => HandleCommand(CommandFind)),
                new(UiText.GoToLine, NativeContextMenuIcon.Locate, () => HandleCommand(CommandGoToLine)),
                null,
                new(
                    UiText.WordWrap,
                    NativeContextMenuIcon.Wrap,
                    () => HandleCommand(CommandWordWrap),
                    Checked: _wordWrap),
                new(
                    UiText.ShowWhitespace,
                    NativeContextMenuIcon.Whitespace,
                    () => HandleCommand(CommandWhitespace),
                    Checked: _showWhitespace),
            ],
            NativeTheme.IsDark(_settings.Theme));
    }

    private void ShowOriginal()
    {
        bool changed = _showAlternative;
        CancelMarkdownSplitterDrag();
        if (_result.Classification.Kind == DocumentKind.Markdown)
        {
            CancelMarkdownPreviewWork();
            _markdownPreview?.SetVisible(false);
            DestroyChild(ref _previewErrorLabel);
            DestroyChild(ref _previewStatusLabel);
            _displayMode = DocumentDisplayMode.Original;
            _markdownPreviewError = null;
        }

        _showAlternative = false;
        _originalEditor?.SetVisible(true);
        _formattedEditor?.SetVisible(false);
        if (changed) ResetFindForDisplayMode();
        Layout();
        InvalidateToolbar();
    }

    private void ShowAlternative()
    {
        if (_result.Classification.Kind == DocumentKind.Markdown)
        {
            _ = SetMarkdownModeAsync(DocumentDisplayMode.Preview);
            return;
        }

        if (_formattedEditor is null || _formattedJson is not { IsValid: true })
        {
            return;
        }

        bool changed = !_showAlternative;
        _showAlternative = true;
        _originalEditor?.SetVisible(false);
        _formattedEditor.SetVisible(true);
        if (changed) ResetFindForDisplayMode();
        Layout();
        InvalidateToolbar();
    }

    private void ResetFindForDisplayMode()
    {
        CancelFindWork();
        _findPosition = 0;
        _lastFindMatch = null;
        _findCountCache = null;
        UpdateFindStatus();
    }


    private void ShowGoToLinePrompt()
    {
        string? input = NativeTextPrompt.Show(
            Handle,
            UiText.GoToLine,
            UiText.LineNumber,
            NativeTheme.IsDark(_settings.Theme));
        if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out int line) && line > 0)
        {
            GoToLine(line);
        }
    }

    private void BringPreviewStatusToFront()
    {
        if (_previewStatusLabel == 0)
        {
            return;
        }

        _ = NativeMethods.SetWindowPosition(
            _previewStatusLabel,
            NativeMethods.WindowPositionTop,
            0,
            0,
            0,
            0,
            NativeMethods.SetWindowPositionNoMove
                | NativeMethods.SetWindowPositionNoSize
                | NativeMethods.SetWindowPositionNoActivate
                | NativeMethods.SetWindowPositionShowWindow);
    }

    private void FindNext(bool backwards)
    {
        if (!_findVisible || _findComposing || ActiveEditor is null || !NativeMethods.IsWindowVisible(Handle)) return;
        UpdateFindStatus();
        if (_findCountJob is not null) _findDirections.Enqueue(backwards);
        else NavigateFindResult(backwards);
    }

    internal static (int Start, int Length)? FindMatch(
        string source,
        string query,
        int position,
        bool backwards,
        bool matchCase,
        bool wholeWord,
        bool regularExpression,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        // 普通文本的计数和定位必须使用相同的大小写比较；正则大小写规则与 OrdinalIgnoreCase 并不等价。
        if (!regularExpression)
            return FindLiteralMatch(source, query, position, backwards, matchCase, wholeWord, cancellationToken);
        Regex regex = CreateFindRegex(query, matchCase, wholeWord, regularExpression, backwards);
        int start = backwards && position < 0 ? source.Length
            : !backwards && position > source.Length ? 0 : Math.Clamp(position, 0, source.Length);
        Match match = regex.Match(source, start);
        if (!match.Success)
        {
            cancellationToken.ThrowIfCancellationRequested();
            match = regex.Match(source, backwards ? source.Length : 0);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return match.Success ? (match.Index, match.Length) : null;
    }

    internal static int CountFindMatches(
        string source,
        string query,
        bool matchCase,
        bool wholeWord,
        bool regularExpression,
        List<(int Start, int Length)>? matches = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0)
        {
            return 0;
        }

        long deadline = Environment.TickCount64 + 250;
        cancellationToken.ThrowIfCancellationRequested();
        if (!regularExpression)
        {
            StringComparison comparison = matchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            int count = 0;
            int iterations = 0;
            int position = 0;
            while (position <= source.Length - query.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int index = source.IndexOf(query, position, comparison);
                if (index < 0)
                {
                    break;
                }

                bool leftBoundary = index == 0 || !IsFindWordCharacter(source[index - 1]);
                int end = index + query.Length;
                bool rightBoundary = end >= source.Length || !IsFindWordCharacter(source[end]);
                bool accepted = !wholeWord || (leftBoundary && rightBoundary);
                if (accepted)
                {
                    count++;
                    matches?.Add((index, query.Length));
                }

                // 拒绝的候选不能遮掉与它重叠、但边界有效的下一个候选。
                position = index + (accepted ? query.Length : 1);
                iterations++;
                if ((iterations & 0xFF) == 0 && Environment.TickCount64 >= deadline)
                {
                    throw new TimeoutException(UiText.FindTimedOut);
                }
            }

            return count;
        }

        Regex regex = CreateFindRegex(query, matchCase, wholeWord, regularExpression: true, backwards: false);
        int regexCount = 0;
        for (Match match = regex.Match(source); match.Success; match = match.NextMatch())
        {
            cancellationToken.ThrowIfCancellationRequested();
            regexCount++;
            matches?.Add((match.Index, match.Length));
            if ((regexCount & 0xFF) == 0 && Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException(UiText.FindTimedOut);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return regexCount;
    }

    private static Regex CreateFindRegex(
        string query,
        bool matchCase,
        bool wholeWord,
        bool regularExpression,
        bool backwards)
    {
        RegexOptions options = RegexOptions.CultureInvariant;
        if (!matchCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        if (backwards)
        {
            options |= RegexOptions.RightToLeft;
        }

        string pattern = regularExpression ? query : Regex.Escape(query);
        if (wholeWord)
        {
            pattern = $"(?<![\\p{{L}}\\p{{N}}_])(?:{pattern})(?![\\p{{L}}\\p{{N}}_])";
        }

        return new(pattern, options, TimeSpan.FromMilliseconds(250));
    }

    private static bool IsFindWordCharacter(char value)
    {
        return value == '_' || char.IsLetter(value) || char.IsNumber(value);
    }

    private void UpdateFindStatus(bool preservePosition = false)
    {
        if (_disposed || _findStatus == 0) return;
        if (_findComposing)
        {
            _findCompositionPreservePosition |= preservePosition;
            return;
        }
        FindQuery query = CurrentFindQuery();
        if (!_findVisible || !NativeMethods.IsWindowVisible(Handle) || query.Query.Length == 0)
        {
            CancelFindWork();
            _findCountCache = null;
            _findMatches = [];
            _findMatchIndex = -1;
            ClearFindHighlights();
            SetFindStatus(string.Empty);
            return;
        }
        if (_findCountCache is { } cached && ReferenceEquals(query.Source, cached.Source)
            && query.Query == cached.Query && query.Case == cached.Case && query.Whole == cached.Whole && query.Regex == cached.Regex)
        {
            SetFindStatus(cached.Text);
            if (!_findHighlightsVisible) ApplyFindHighlights(query.Source);
            return;
        }
        if (_findCountJob is { } pending && pending.Query.SameAs(query)) return;
        int? previousMatchStart = preservePosition ? _lastFindMatch?.Start : null;
        CancelFindWork();
        _findCountCache = null;
        _lastFindMatch = null;
        _findMatches = [];
        _findMatchIndex = -1;
        ClearFindHighlights();
        SetFindStatus(string.Empty);
        if (query.Background)
        {
            FindJob job = new(query);
            _findCountJob = job;
            FindStatusScanCountForTest++;
            _findCountWorker = RunFindJobAsync(job, _findCountWorker, token =>
            {
                List<(int Start, int Length)> matches = [];
                _ = CountFindMatches(query.Source, query.Query, query.Case, query.Whole, query.Regex, matches, token);
                return () =>
                {
                    _findCountJob = null;
                    CompleteFindQuery(query, matches, previousMatchStart, preservePosition);
                };
            });
            return;
        }
        try
        {
            FindStatusScanCountForTest++;
            List<(int Start, int Length)> matches = [];
            _ = CountFindMatches(query.Source, query.Query, query.Case, query.Whole, query.Regex, matches: matches);
            CompleteFindQuery(query, matches, previousMatchStart, preservePosition);
        }
        catch (TimeoutException) { SetFindStatus(UiText.FindTimedOut); }
        catch (ArgumentException) { SetFindStatus(UiText.InvalidRegularExpression); }
    }

    private void SetFindStatus(string text)
    {
        if (NativeMethods.GetWindowTextValue(_findStatus) == text) return;
        _ = NativeMethods.SetWindowText(_findStatus, text);
        string description = string.IsNullOrEmpty(text) ? UiText.Find : text;
        if (_findStatusTipAdded) _documentToolTip?.Update(_findStatus, description);
        else { _documentToolTip?.Add(_findStatus, description); _findStatusTipAdded = true; }
        _ = NativeMethods.InvalidateRectangle(_findStatus, 0, true);
        if (MeasureFindStatusWidth() && _findVisible) Layout();
    }

    private bool MeasureFindStatusWidth()
    {
        string text = NativeMethods.GetWindowTextValue(_findStatus);
        nint context = NativeMethods.GetDeviceContext(Handle);
        nint previous = NativeMethods.SelectObject(context, NativeTheme.UiFont);
        int next;
        try
        {
            NativeMethods.Rectangle bounds = new();
            _ = NativeMethods.DrawText(context, text, text.Length, ref bounds,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
            next = Math.Max(FindStatusPreferredWidth, bounds.Right - bounds.Left + NativeTheme.Scale(4));
        }
        finally
        {
            _ = NativeMethods.SelectObject(context, previous);
            _ = NativeMethods.ReleaseDeviceContext(Handle, context);
        }
        bool changed = next != _findStatusWidth;
        _findStatusWidth = next;
        return changed;
    }

    private void SetFindBarVisible(bool visible)
    {
        foreach (nint control in new[]
        {
            _findOverlay,
            _findEdit,
            _matchCaseButton,
            _wholeWordButton,
            _regularExpressionButton,
            _findPreviousButton,
            _findNextButton,
            _findCloseButton,
            _findStatus,
        })
        {
            if (control != 0)
            {
                _ = NativeMethods.ShowWindow(control, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
            }
        }
    }

    private void SetImageToolbarVisible(bool visible)
    {
        foreach (nint control in _imageToolbarControls)
        {
            if (control != 0)
            {
                _ = NativeMethods.ShowWindow(control, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
            }
        }
    }

    private void SetBlameToolbarVisible(bool visible, int lineCount = 0)
    {
        if (!visible && !_showBlame) return;
        _showBlame = visible;
        if (visible)
        {
            string count = UiText.BlameLineCount(lineCount);
            _ = NativeMethods.SetWindowText(_blameCountLabel, count);
            _documentToolTip?.Update(_blameCountLabel, count);
        }

        _ = NativeMethods.ShowWindow(_blameCountLabel, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_blameCloseButton, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_breadcrumbLabel, visible ? NativeMethods.ShowHide : NativeMethods.ShowNormal);
        foreach (nint control in _toolbarControls)
        {
            _ = NativeMethods.ShowWindow(control, visible ? NativeMethods.ShowHide : NativeMethods.ShowNormal);
        }

        if (!visible)
        {
            bool textReady = _result.Status == DocumentReadStatus.TextReady;
            bool plainText = textReady && _result.Classification.Kind == DocumentKind.Text;
            foreach (nint control in new[] { _wordWrapButton, _whitespaceButton, _findButton, _goToButton })
            {
                _ = NativeMethods.ShowWindow(control, plainText ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
            }

            _ = NativeMethods.ShowWindow(_moreButton,
                textReady && !plainText ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
            _ = NativeMethods.ShowWindow(_originalButton,
                textReady && _result.Classification.Kind is DocumentKind.Json or DocumentKind.Markdown
                    ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
            _ = NativeMethods.ShowWindow(_alternativeButton,
                textReady && _result.Classification.Kind is DocumentKind.Json or DocumentKind.Markdown
                    ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
            _ = NativeMethods.ShowWindow(_splitButton,
                textReady && _result.Classification.Kind == DocumentKind.Markdown
                    ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
            _modeSegmentVisible = textReady
                && _result.Classification.Kind is DocumentKind.Json or DocumentKind.Markdown;
        }
        else
        {
            _modeSegmentVisible = false;
        }
        Layout();
        InvalidateToolbar();
    }

    private void UpdateImageToolbarState()
    {
        if (_imageView is null)
        {
            return;
        }

        string zoomText = IsImagePreviewReady ? $"{_imageView.ZoomPercentage}%" : string.Empty;
        if (NativeMethods.GetWindowTextValue(_imageZoomLabel) != zoomText)
        {
            _ = NativeMethods.SetWindowText(_imageZoomLabel, zoomText);
            _imageToolTip?.Update(_imageZoomLabel, zoomText);
        }
        _ = NativeMethods.EnableWindow(_imageZoomOutButton, _imageView.CanZoomOut);
        _ = NativeMethods.EnableWindow(_imageZoomInButton, _imageView.CanZoomIn);
        _ = NativeMethods.EnableWindow(_imageFitButton, IsImagePreviewReady);
        foreach (nint control in _imageToolbarControls)
        {
            _ = NativeMethods.InvalidateRectangle(control, 0, true);
        }
    }

    private static string GetSummaryDisplayType(DocumentKind kind, string fallback)
    {
        return kind switch
        {
            DocumentKind.Gif => "GIF 图片",
            DocumentKind.WebP => "WebP 图片",
            _ => fallback,
        };
    }

    private void Layout()
    {
        if (Handle == 0 || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = Math.Max(0, client.Right - client.Left);
        int height = Math.Max(0, client.Bottom - client.Top);
        int toolbarRight = Math.Max(NativeTheme.Scale(8), width - NativeTheme.Scale(8));
        if (_showBlame)
        {
            int button = FindButtonSize;
            int gap = NativeTheme.Scale(4);
            Move(_blameCloseButton, Math.Max(0, width - NativeTheme.Scale(8) - button),
                (ToolbarHeight - button) / 2, button, button);
            int labelRight = Math.Max(NativeTheme.Scale(9), width - NativeTheme.Scale(8) - button - gap);
            Move(_blameCountLabel, NativeTheme.Scale(9), 0,
                Math.Max(0, labelRight - NativeTheme.Scale(9)), ToolbarHeight);
        }
        if (_imageView is not null)
        {
            LayoutImageToolbar(width);
        }

        if (NativeMethods.IsWindowVisible(_wordWrapButton))
        {
            int toolbarTop = (ToolbarHeight - FindButtonSize) / 2;
            foreach (nint control in new[]
            {
                _goToButton,
                _findButton,
                _whitespaceButton,
                _wordWrapButton,
            })
            {
                toolbarRight -= FindButtonSize;
                Move(control, toolbarRight, toolbarTop, FindButtonSize, FindButtonSize);
                toolbarRight -= NativeTheme.Scale(4);
            }
        }

        if (NativeMethods.IsWindowVisible(_moreButton))
        {
            toolbarRight -= FindButtonSize;
            Move(
                _moreButton,
                toolbarRight,
                (ToolbarHeight - FindButtonSize) / 2,
                FindButtonSize,
                FindButtonSize);
            toolbarRight -= NativeTheme.Scale(4);
        }

        int modeButtonCount = 0;
        if (NativeMethods.IsWindowVisible(_originalButton))
        {
            modeButtonCount++;
        }

        if (NativeMethods.IsWindowVisible(_alternativeButton))
        {
            modeButtonCount++;
        }

        if (NativeMethods.IsWindowVisible(_splitButton))
        {
            modeButtonCount++;
        }

        _modeSegmentVisible = modeButtonCount > 0
            && _result.Status == DocumentReadStatus.TextReady
            && _result.Classification.Kind is DocumentKind.Json or DocumentKind.Markdown;
        if (_modeSegmentVisible)
        {
            int segmentWidth = CalculateModeSegmentWidth(modeButtonCount);
            toolbarRight -= segmentWidth;
            int segmentLeft = toolbarRight;
            _modeSegmentRectangle = new NativeMethods.Rectangle
            {
                Left = segmentLeft,
                Top = (ToolbarHeight - ModeButtonHeight) / 2,
                Right = segmentLeft + segmentWidth,
                Bottom = (ToolbarHeight + ModeButtonHeight) / 2,
            };
            int buttonTop = (ToolbarHeight - ModeButtonHeight) / 2;
            int buttonLeft = segmentLeft + ModeSegmentPadding;
            if (NativeMethods.IsWindowVisible(_originalButton))
            {
                Move(
                    _originalButton,
                    buttonLeft,
                    buttonTop,
                    ModeButtonWidth,
                    ModeButtonHeight);
                buttonLeft += ModeButtonWidth + ModeSegmentGap;
            }

            if (NativeMethods.IsWindowVisible(_splitButton))
            {
                Move(
                    _splitButton,
                    buttonLeft,
                    buttonTop,
                    ModeButtonWidth,
                    ModeButtonHeight);
                buttonLeft += ModeButtonWidth + ModeSegmentGap;
            }

            if (NativeMethods.IsWindowVisible(_alternativeButton))
            {
                Move(
                    _alternativeButton,
                    buttonLeft,
                    buttonTop,
                    ModeButtonWidth,
                    ModeButtonHeight);
            }

            toolbarRight -= NativeTheme.Scale(4);
        }
        else
        {
            _modeSegmentRectangle = default;
        }

        Move(
            _breadcrumbLabel,
            NativeTheme.Scale(9),
            0,
            Math.Max(0, toolbarRight - NativeTheme.Scale(13)),
            ToolbarHeight);

        int contentTop = _infoView is not null ? 0 : ToolbarHeight;
        if (_targetLabel != 0 && NativeMethods.IsWindowVisible(_targetLabel))
        {
            int targetHeight = MeasureDocumentNotice(_targetLabel, width, 32);
            Move(_targetLabel, 0, contentTop, width, targetHeight);
            contentTop += targetHeight;
        }

        if (_formattedJson is { IsValid: false } && _jsonErrorButton != 0)
        {
            int errorHeight = MeasureJsonErrorHeight(width);
            Move(_jsonErrorButton, 0, contentTop, width, errorHeight);
            contentTop += errorHeight;
        }

        if (_findVisible)
        {
            (int findX, int findY, int findWidth, int findHeight, int editWidth, int statusWidth) =
                CalculateFindOverlayLayout(width, contentTop, _findStatusWidth);
            Move(_findOverlay, findX, findY, findWidth, findHeight);
            contentTop += findHeight;
            int findButtonSize = GetFindOverlayButtonSize(findWidth);
            int controlY = findY + (findHeight - findButtonSize) / 2;
            int editHeight = FindEditHeight;
            int controlX = findX + FindOverlayPadding;
            _findEditFrame = new()
            {
                Left = FindOverlayPadding,
                Top = (findHeight - editHeight) / 2,
                Right = FindOverlayPadding + editWidth,
                Bottom = (findHeight + editHeight) / 2,
            };
            int textHeight = NativeTheme.UiLineHeight;
            Move(
                _findEdit,
                controlX + NativeTheme.Scale(8),
                findY + (findHeight - textHeight) / 2,
                Math.Max(0, editWidth - NativeTheme.Scale(16)),
                textHeight);
            _ = NativeMethods.SendMessage(_findEdit, 0x00D3, 3, 0);
            _ = NativeMethods.InvalidateRectangle(_findOverlay, 0, false);
            controlX += editWidth + FindOverlayGap;
            Move(_matchCaseButton, controlX, controlY, findButtonSize, findButtonSize);
            controlX += findButtonSize + FindOverlayGap;
            Move(_wholeWordButton, controlX, controlY, findButtonSize, findButtonSize);
            controlX += findButtonSize + FindOverlayGap;
            Move(_regularExpressionButton, controlX, controlY, findButtonSize, findButtonSize);
            controlX += findButtonSize + FindOverlayGap;
            Move(_findStatus, controlX, findY + (findHeight - editHeight) / 2, statusWidth, editHeight);
            controlX += statusWidth + FindOverlayGap;
            Move(_findPreviousButton, controlX, controlY, findButtonSize, findButtonSize);
            controlX += findButtonSize + FindOverlayGap;
            Move(_findNextButton, controlX, controlY, findButtonSize, findButtonSize);
            controlX += findButtonSize + FindOverlayGap;
            Move(_findCloseButton, controlX, controlY, findButtonSize, findButtonSize);
        }

        LayoutDocumentBody(width, height, contentTop);
        BringFindOverlayToFront();
    }

    private void LayoutDocumentBody(int width, int height, int contentTop)
    {
        int contentHeight = Math.Max(0, height - contentTop);
        bool markdownSplit = IsMarkdownSplitMode();
        int originalWidth = markdownSplit
            ? GetMarkdownSplitSourceWidth(width)
            : width;
        _originalEditor?.SetBounds(0, contentTop, originalWidth, contentHeight);
        _formattedEditor?.SetBounds(0, contentTop, width, contentHeight);
        if (_markdownPreview is not null && _displayMode != DocumentDisplayMode.Original)
        {
            int previewX = markdownSplit ? originalWidth + MarkdownSplitterWidth : 0;
            _markdownPreview.SetBounds(previewX, contentTop, Math.Max(0, width - previewX), contentHeight);
            _markdownPreview.SetVisible(NativeMethods.IsWindowVisible(Handle));
        }

        if (_previewErrorLabel != 0)
        {
            int previewX = markdownSplit ? originalWidth + MarkdownSplitterWidth : 0;
            int previewWidth = Math.Max(0, width - previewX);
            int errorHeight = MeasureDocumentNotice(_previewErrorLabel, previewWidth, 42);
            Move(_previewErrorLabel, previewX, contentTop, previewWidth, Math.Min(contentHeight, errorHeight));
            _ = NativeMethods.SetWindowPosition(_previewErrorLabel, NativeMethods.WindowPositionTop, 0, 0, 0, 0,
                NativeMethods.SetWindowPositionNoMove | NativeMethods.SetWindowPositionNoSize | NativeMethods.SetWindowPositionNoActivate);
        }
        if (_previewStatusLabel != 0)
        {
            int previewX = markdownSplit ? originalWidth + MarkdownSplitterWidth : 0;
            Move(_previewStatusLabel, previewX, contentTop, Math.Max(0, width - previewX), NativeTheme.ContentHeight(36, 8));
            BringPreviewStatusToFront();
        }
        if (_imageView is not null)
        {
            _imageView.SetBounds(0, contentTop, width, contentHeight);
            UpdateImageToolbarState();
        }
        _infoView?.SetBounds(0, 0, width, height);
    }

    private bool IsMarkdownSplitMode()
    {
        return _result.Classification.Kind == DocumentKind.Markdown
            && _displayMode == DocumentDisplayMode.Split;
    }

    private int GetContentTop()
    {
        int contentTop = _infoView is not null ? 0 : ToolbarHeight;
        if (_targetLabel != 0 && NativeMethods.IsWindowVisible(_targetLabel))
        {
            _ = NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle targetClient);
            contentTop += MeasureDocumentNotice(_targetLabel, targetClient.Right, 32);
        }

        if (_formattedJson is { IsValid: false } && _jsonErrorButton != 0
            && NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle rectangle))
        {
            contentTop += MeasureJsonErrorHeight(rectangle.Right);
        }

        // 查找条占据正文顶部；分隔线绘制与拖动命中必须使用正文实际起点。
        return contentTop + (_findVisible ? FindOverlayHeight : 0);
    }

    private bool TryGetClientCursorPosition(out NativeMethods.Point point)
    {
        point = default;
        return NativeMethods.GetCursorPosition(out point)
            && NativeMethods.ScreenToClient(Handle, ref point);
    }

    private bool IsMarkdownSplitterHit(int x, int y)
    {
        if (!IsMarkdownSplitMode()
            || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client)
            || y < GetContentTop()
            || y >= client.Bottom)
        {
            return false;
        }

        int sourceWidth = GetMarkdownSplitSourceWidth(client.Right - client.Left);
        int splitterLeft = sourceWidth;
        int splitterRight = sourceWidth + MarkdownSplitterWidth;
        return x >= splitterLeft - NativeTheme.Scale(4)
            && x <= splitterRight + NativeTheme.Scale(4);
    }

    private bool TrySetMarkdownSplitterCursor()
    {
        if (!TryGetClientCursorPosition(out NativeMethods.Point point)
            || !IsMarkdownSplitterHit(point.X, point.Y))
        {
            return false;
        }

        _ = NativeMethods.SetCursor(NativeMethods.LoadCursor(0, NativeMethods.SizeWestEastCursor));
        return true;
    }

    private bool BeginMarkdownSplitterDrag(nint longParameter)
    {
        if (_disposed || !IsMarkdownSplitMode()
            || !NativeMethods.IsWindowVisible(Handle) || !NativeMethods.IsWindowEnabled(Handle))
        {
            return false;
        }

        int x = DecodeClientCoordinate(NativeMethods.LowWord(unchecked((nuint)longParameter)));
        int y = DecodeClientCoordinate(NativeMethods.HighWord(unchecked((nuint)longParameter)));
        if (!IsMarkdownSplitterHit(x, y))
        {
            return false;
        }

        if (_draggingMarkdownSplitter && NativeMethods.GetCapture() == Handle) return true;
        if (!NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client))
        {
            return false;
        }

        int sourceWidth = GetMarkdownSplitSourceWidth(client.Right - client.Left);
        _markdownSplitterGrabOffset = x - (sourceWidth + MarkdownSplitterWidth / 2);
        _draggingMarkdownSplitter = true;
        _ = NativeMethods.SetCapture(Handle);
        if (NativeMethods.GetCapture() == Handle) return true;
        CancelMarkdownSplitterDrag();
        return false;
    }

    private bool UpdateMarkdownSplitterDrag(nint longParameter)
    {
        if (!_draggingMarkdownSplitter) return false;
        if (_disposed || NativeMethods.GetCapture() != Handle
            || !NativeMethods.IsWindowVisible(Handle) || !IsMarkdownSplitMode()
            || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client))
        {
            CancelMarkdownSplitterDrag();
            return false;
        }

        int x = DecodeClientCoordinate(NativeMethods.LowWord(unchecked((nuint)longParameter)));
        int availableWidth = Math.Max(0, client.Right - client.Left - MarkdownSplitterWidth);
        if (availableWidth <= NativeTheme.Scale(240) * 2)
        {
            // 两侧都受最小宽度限制时不改记忆比例，避免奇数宽度的 1px 差改变恢复后的布局。
            return true;
        }

        // 捕获期间坐标可能为负；保留按下偏移，按实际可见宽度夹紧并去重。
        double ratio = (x - _markdownSplitterGrabOffset - MarkdownSplitterWidth / 2) / (double)availableWidth;
        int sourceWidth = CalculateMarkdownSplitSourceWidth(client.Right - client.Left, ratio);
        int currentSourceWidth = GetMarkdownSplitSourceWidth(client.Right - client.Left);
        if (sourceWidth == currentSourceWidth)
        {
            return true;
        }

        _markdownSplitRatio = sourceWidth / (double)availableWidth;
        int contentTop = GetContentTop();
        LayoutDocumentBody(client.Right - client.Left, client.Bottom - client.Top, contentTop);
        client.Top = Math.Min(client.Bottom, contentTop);
        _ = NativeMethods.InvalidateRectangle(Handle, ref client, true);
        return true;
    }

    private bool EndMarkdownSplitterDrag(nint longParameter)
    {
        if (!_draggingMarkdownSplitter)
        {
            return false;
        }

        UpdateMarkdownSplitterDrag(longParameter);
        CancelMarkdownSplitterDrag();
        return true;
    }

    internal bool TryCancelMarkdownSplitterDrag()
    {
        if (!_draggingMarkdownSplitter) return false;
        CancelMarkdownSplitterDrag();
        return true;
    }

    private void CancelMarkdownSplitterDrag()
    {
        _draggingMarkdownSplitter = false;
        _markdownSplitterGrabOffset = 0;
        if (Handle != 0 && NativeMethods.GetCapture() == Handle)
        {
            _ = NativeMethods.ReleaseCapture();
        }
    }

    private static int DecodeClientCoordinate(int value) => unchecked((short)value);

    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
    }

    private static void ApplyRoundedRegion(nint window, int width, int height, int radius)
    {
        if (window == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        nint region = NativeMethods.CreateRoundRectangleRegion(
            0,
            0,
            width + 1,
            height + 1,
            Math.Max(1, radius * 2),
            Math.Max(1, radius * 2));
        if (region != 0 && NativeMethods.SetWindowRegion(window, region, true) == 0)
        {
            _ = NativeMethods.DeleteObject(region);
        }
    }

    private static (int X, int Y, int Width, int Height, int EditWidth, int StatusWidth)
        CalculateFindOverlayLayout(int width, int contentTop, int requestedStatusWidth = 0)
    {
        int availableWidth = Math.Max(0, width);
        int preferredStatusWidth = Math.Max(FindStatusPreferredWidth, requestedStatusWidth);
        int overlayWidth = availableWidth;
        int buttonSize = GetFindOverlayButtonSize(overlayWidth);
        int fixedWidth = FindOverlayPadding * 2
            + buttonSize * 6
            + FindOverlayGap * 7;
        int flexibleWidth = Math.Max(0, overlayWidth - fixedWidth);
        int statusWidth = Math.Min(
            preferredStatusWidth,
            Math.Max(0, flexibleWidth - FindEditMinimumWidth));
        int editWidth = Math.Max(0, flexibleWidth - statusWidth);
        int x = 0;
        int y = contentTop;
        return (x, y, overlayWidth, FindOverlayHeight, editWidth, statusWidth);
    }

    private static int GetFindOverlayButtonSize(int width)
    {
        int preferred = FindButtonSize;
        int fixedWithoutButtons = FindOverlayPadding * 2 + FindOverlayGap * 7;
        int available = Math.Max(0, width - fixedWithoutButtons);
        // 极窄容器下允许继续缩小，确保六个按钮与间距不会把子控件推到正文之外。
        return Math.Min(preferred, available / 6);
    }

    private void BringFindOverlayToFront()
    {
        if (!_findVisible)
        {
            return;
        }

        foreach (nint control in new[]
        {
            _findOverlay,
            _findEdit,
            _matchCaseButton,
            _wholeWordButton,
            _regularExpressionButton,
            _findStatus,
            _findPreviousButton,
            _findNextButton,
            _findCloseButton,
        })
        {
            if (control == 0)
            {
                continue;
            }

            _ = NativeMethods.SetWindowPosition(
                control,
                NativeMethods.WindowPositionTop,
                0,
                0,
                0,
                0,
                NativeMethods.SetWindowPositionNoMove
                    | NativeMethods.SetWindowPositionNoSize
                    | NativeMethods.SetWindowPositionNoActivate
                    | NativeMethods.SetWindowPositionShowWindow);
        }
    }

    private bool IsControlWithinClient(nint control)
    {
        if (control == 0
            || !NativeMethods.IsWindowVisible(control)
            || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client)
            || !NativeMethods.GetWindowRectangle(control, out NativeMethods.Rectangle rectangle))
        {
            return false;
        }

        NativeMethods.Point topLeft = new() { X = rectangle.Left, Y = rectangle.Top };
        NativeMethods.Point bottomRight = new() { X = rectangle.Right, Y = rectangle.Bottom };
        return NativeMethods.ScreenToClient(Handle, ref topLeft)
            && NativeMethods.ScreenToClient(Handle, ref bottomRight)
            && topLeft.X >= 0
            && topLeft.Y >= 0
            && bottomRight.X <= client.Right
            && bottomRight.Y <= client.Bottom;
    }

    private bool IsControlWithinToolbar(nint control)
    {
        if (!IsControlWithinClient(control)
            || !NativeMethods.GetWindowRectangle(control, out NativeMethods.Rectangle rectangle))
        {
            return false;
        }

        NativeMethods.Point bottomRight = new() { X = rectangle.Right, Y = rectangle.Bottom };
        return NativeMethods.ScreenToClient(Handle, ref bottomRight)
            && bottomRight.Y <= ToolbarHeight;
    }

    private bool IsModeSegmentWithinToolbar()
    {
        if (!_modeSegmentVisible
            || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client))
        {
            return false;
        }

        return _modeSegmentRectangle.Left >= 0
            && _modeSegmentRectangle.Top >= 0
            && _modeSegmentRectangle.Right <= client.Right
            && _modeSegmentRectangle.Bottom <= Math.Min(client.Bottom, ToolbarHeight);
    }

    private static nint PackClientPoint(int x, int y)
    {
        return unchecked((nint)((ushort)x | (uint)(ushort)y << 16));
    }

    private int GetMarkdownSplitSourceWidth(int width)
    {
        return CalculateMarkdownSplitSourceWidth(width, _markdownSplitRatio);
    }

    private static int CalculateMarkdownSplitSourceWidth(int width, double ratio)
    {
        if (width <= 0)
        {
            return 0;
        }

        int availableWidth = Math.Max(0, width - MarkdownSplitterWidth);
        int minimumSideWidth = Math.Min(availableWidth / 2, NativeTheme.Scale(240));
        int minimumSourceWidth = minimumSideWidth;
        int maximumSourceWidth = Math.Max(minimumSourceWidth, availableWidth - minimumSideWidth);
        // 视觉稿的左右对照使用等宽两栏，窄窗口再通过上下限保护正文可读宽度。
        int preferredSourceWidth = (int)Math.Round(availableWidth * Math.Clamp(ratio, 0d, 1d));
        return Math.Clamp(preferredSourceWidth, minimumSourceWidth, maximumSourceWidth);
    }

    private static int CalculateModeSegmentWidth(int buttonCount)
    {
        if (buttonCount <= 0)
        {
            return 0;
        }

        return ModeSegmentPadding * 2
            + ModeButtonWidth * buttonCount
            + ModeSegmentGap * Math.Max(0, buttonCount - 1);
    }

    private bool IsModeButtonSelected(int controlIdentifier)
    {
        return _result.Classification.Kind == DocumentKind.Markdown
            ? controlIdentifier switch
            {
                CommandOriginal => _displayMode == DocumentDisplayMode.Original,
                CommandAlternative => _displayMode == DocumentDisplayMode.Preview,
                CommandSplit => _displayMode == DocumentDisplayMode.Split,
                _ => false,
            }
            : controlIdentifier switch
            {
                CommandOriginal => !_showAlternative,
                CommandAlternative => _showAlternative,
                _ => false,
            };
    }

    private static NativeDocumentModeIcon ResolveDocumentModeIcon(
        DocumentKind kind,
        uint controlIdentifier)
    {
        return controlIdentifier switch
        {
            CommandOriginal => NativeDocumentModeIcon.Source,
            CommandSplit => NativeDocumentModeIcon.Split,
            _ => kind == DocumentKind.Json
                ? NativeDocumentModeIcon.Formatted
                : NativeDocumentModeIcon.Preview,
        };
    }

    internal static NativeDocumentModeIcon ResolveDocumentModeIconForTest(
        DocumentKind kind,
        bool alternativeButton)
    {
        return ResolveDocumentModeIcon(
            kind,
            alternativeButton ? (uint)CommandAlternative : (uint)CommandOriginal);
    }

    private bool IsChecked(nint button)
    {
        return button == _wordWrapButton ? _wordWrap
            : button == _whitespaceButton ? _showWhitespace
            : button == _matchCaseButton ? _matchCase
            : button == _wholeWordButton ? _wholeWord
            : button == _regularExpressionButton && _regularExpression;
    }

    private void ShowLaunchResult(ExternalLaunchResult result)
    {
        if (!result.IsSuccess)
        {
            _setStatus(result.ErrorMessage ?? UiText.ExternalProgramFailed);
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:F1} KB";
        }

        return $"{bytes / 1024d / 1024d:F1} MB";
    }

    private enum DocumentDisplayMode
    {
        Original,
        Preview,
        Split,
    }
}
