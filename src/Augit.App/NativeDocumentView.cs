using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Augit.Core.Documents;
using Augit.Infrastructure.Interop;
using Augit.Infrastructure.Settings;
using Microsoft.Web.WebView2.Core;

namespace Augit.App;

internal sealed class NativeDocumentView : IDisposable
{
    private const string WindowClassName = "Augit.DocumentView.Native";
    private const int ToolbarHeight = 36;
    private const int FindBarHeight = 36;
    private const int CommandOriginal = 1;
    private const int CommandAlternative = 2;
    private const int CommandSplit = 3;
    private const int CommandFind = 4;
    private const int CommandGoToLine = 5;
    private const int CommandWordWrap = 6;
    private const int CommandWhitespace = 7;
    private const int CommandOpenExternal = 8;
    private const int CommandFindPrevious = 9;
    private const int CommandFindNext = 10;
    private const int CommandCloseFind = 11;
    private const int CommandMatchCase = 12;
    private const int CommandWholeWord = 13;
    private const int CommandRegularExpression = 14;
    private const int FindEditIdentifier = 20;
    private const int EditNotificationChanged = 0x0300;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeDocumentView> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly string _workspaceRoot;
    private readonly Action<string> _setStatus;
    private readonly Action<string, int?, string?> _openLinkedFile;
    private readonly List<nint> _toolbarControls = [];
    private ApplicationSettings _settings;
    private DocumentReadResult _result;
    private ScintillaControl? _originalEditor;
    private ScintillaControl? _formattedEditor;
    private NativeImageView? _imageView;
    private MarkdownWebViewHost? _markdownPreview;
    private nint _previewErrorLabel;
    private nint _summaryLabel;
    private nint _targetLabel;
    private nint _openExternalButton;
    private nint _originalButton;
    private nint _alternativeButton;
    private nint _splitButton;
    private nint _findButton;
    private nint _goToButton;
    private nint _wordWrapButton;
    private nint _whitespaceButton;
    private nint _findEdit;
    private nint _findPreviousButton;
    private nint _findNextButton;
    private nint _findCloseButton;
    private nint _matchCaseButton;
    private nint _wholeWordButton;
    private nint _regularExpressionButton;
    private nint _findStatus;
    private bool _findVisible;
    private bool _showAlternative;
    private DocumentDisplayMode _displayMode;
    private int _markdownPreviewVersion;
    private bool _wordWrap;
    private bool _showWhitespace;
    private int _findPosition;
    private string? _pendingAnchor;
    private string? _markdownPreviewError;
    private bool _disposed;

    internal NativeDocumentView(
        nint parent,
        string workspaceRoot,
        DocumentReadResult result,
        ApplicationSettings settings,
        Action<string> setStatus,
        Action<string, int?, string?> openLinkedFile)
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
        CreateFindBar();
        ApplyResult();
        ApplyAppearance(settings);
        Layout();
    }

    internal nint Handle { get; private set; }

    internal string Path => _result.RequestedPath;

    internal bool IsMarkdownPreviewReady => _markdownPreview is not null;

    internal string? MarkdownPreviewError => _markdownPreviewError;

    internal bool IsImagePreviewReady => _imageView is not null;

    internal string? CurrentText => _result.Text;

    internal bool IsTextReadOnly => _originalEditor?.IsReadOnly != false;

    internal DocumentReadStatus Status => _result.Status;

    internal DocumentKind Kind => _result.Classification.Kind;

    internal bool IsShowingAlternative => _showAlternative;

    internal int MarkdownBrowserProcessId => _markdownPreview?.BrowserProcessId ?? 0;

    internal void SetBounds(int x, int y, int width, int height)
    {
        _ = NativeMethods.MoveWindow(Handle, x, y, Math.Max(0, width), Math.Max(0, height), true);
        Layout();
    }

    internal void SetVisible(bool visible)
    {
        _ = NativeMethods.ShowWindow(Handle, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
    }

    internal void ApplyAppearance(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        bool dark = NativeTheme.IsDark(settings.Theme);
        _originalEditor?.ApplyAppearance(settings.MonospaceFontFamily, settings.FontSize, dark);
        _formattedEditor?.ApplyAppearance(settings.MonospaceFontFamily, settings.FontSize, dark);
        if (_result.Classification.Kind == DocumentKind.Markdown
            && _markdownPreview is not null
            && _displayMode is DocumentDisplayMode.Preview or DocumentDisplayMode.Split)
        {
            _ = SetMarkdownModeAsync(_displayMode);
        }
    }

    internal Task ReloadAsync(DocumentReadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _result = result;
        ApplyResult();
        ApplyAppearance(_settings);
        Layout();
        return Task.CompletedTask;
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
        _ = NativeMethods.SetFocus(_findEdit);
    }

    internal void HideFind()
    {
        _findVisible = false;
        SetFindBarVisible(false);
        Layout();
    }

    internal void GoToLine(int lineNumber)
    {
        ActiveEditor?.GoToLine(lineNumber);
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

    internal int CloseMarkdownPreviewForTest()
    {
        int processId = MarkdownBrowserProcessId;
        ShowOriginal();
        return processId;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _originalEditor?.Dispose();
        _formattedEditor?.Dispose();
        _imageView?.Dispose();
        _markdownPreview?.Dispose();
        _originalEditor = null;
        _formattedEditor = null;
        _imageView = null;
        _markdownPreview = null;
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
        ? JsonDisplayFormatter.Format(_result.Text ?? string.Empty).DisplayText
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
            case NativeMethods.WindowMessageSize:
                instance.Layout();
                return 0;
            case NativeMethods.WindowMessageCommand:
                instance.HandleCommand(wordParameter);
                return 0;
            case NativeMethods.WindowMessageSetFocus:
                if (instance.ActiveEditor is not null)
                {
                    _ = NativeMethods.SetFocus(instance.ActiveEditor.Handle);
                }

                return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private void CreateToolbar()
    {
        _originalButton = CreateButton(UiText.Original, CommandOriginal);
        _alternativeButton = CreateButton(UiText.Preview, CommandAlternative);
        _splitButton = CreateButton(UiText.Split, CommandSplit);
        _findButton = CreateButton(UiText.Find, CommandFind);
        _goToButton = CreateButton(UiText.GoToLine, CommandGoToLine);
        _wordWrapButton = CreateCheckbox(UiText.WordWrap, CommandWordWrap);
        _whitespaceButton = CreateCheckbox(UiText.ShowWhitespace, CommandWhitespace);
    }

    private void CreateFindBar()
    {
        _findEdit = CreateChild(
            NativeMethods.EditClass,
            string.Empty,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleTabStop | NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            FindEditIdentifier);
        _matchCaseButton = CreateCheckbox(UiText.MatchCase, CommandMatchCase, false);
        _wholeWordButton = CreateCheckbox(UiText.MatchWholeWord, CommandWholeWord, false);
        _regularExpressionButton = CreateCheckbox(UiText.RegularExpression, CommandRegularExpression, false);
        _findPreviousButton = CreateButton(UiText.Previous, CommandFindPrevious, false);
        _findNextButton = CreateButton(UiText.Next, CommandFindNext, false);
        _findCloseButton = CreateButton(UiText.CloseSymbol, CommandCloseFind, false);
        _findStatus = CreateChild(NativeMethods.StaticClass, string.Empty, NativeMethods.WindowStyleChild | NativeMethods.StaticLeft, 0);
        SetFindBarVisible(false);
    }

    private nint CreateButton(string text, int command, bool toolbar = true)
    {
        nint handle = CreateChild(
            NativeMethods.ButtonClass,
            text,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.WindowStyleTabStop | NativeMethods.ButtonPushButton,
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
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.WindowStyleTabStop | NativeMethods.ButtonAutoCheckbox,
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

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(child, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        return child;
    }

    private void ApplyResult()
    {
        DisposeContentControls();
        _showAlternative = false;
        _markdownPreviewError = null;
        _findPosition = 0;
        bool textReady = _result.Status == DocumentReadStatus.TextReady;
        if (!_result.RequestedPath.Equals(_result.ResolvedPath, StringComparison.OrdinalIgnoreCase))
        {
            _targetLabel = CreateChild(
                NativeMethods.StaticClass,
                UiText.SymbolicLinkTarget(_result.ResolvedPath),
                NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.StaticLeft,
                0);
        }

        foreach (nint control in _toolbarControls)
        {
            _ = NativeMethods.ShowWindow(control, textReady ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        }

        if (textReady)
        {
            CreateTextContent();
            if (_result.Classification.Kind == DocumentKind.Markdown)
            {
                _ = SetMarkdownModeAsync(DocumentDisplayMode.Preview);
            }

            return;
        }

        if (_result.Status == DocumentReadStatus.ImageReady)
        {
            try
            {
                _imageView = new(Handle);
                _imageView.SetBitmap(WicBitmapLoader.Load(_result.ResolvedPath));
                _openExternalButton = CreateButton(UiText.OpenExternally, CommandOpenExternal, false);
                return;
            }
            catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                ShowSummary(UiText.ImageDecodeFailed);
                return;
            }
        }

        ShowSummary();
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
            _formattedEditor = new(Handle, 101);
            _formattedEditor.SetTextContent(formatted.DisplayText);
            _showAlternative = formatted.IsValid;
            _originalEditor.SetVisible(!formatted.IsValid);
            _formattedEditor.SetVisible(formatted.IsValid);
            _ = NativeMethods.SetWindowText(_alternativeButton, UiText.Formatted);
            _ = NativeMethods.ShowWindow(_alternativeButton, NativeMethods.ShowNormal);
            _ = NativeMethods.ShowWindow(_splitButton, NativeMethods.ShowHide);
            if (!formatted.IsValid)
            {
                _setStatus(UiText.JsonError(formatted.ErrorLine, formatted.ErrorColumn));
            }
        }
        else if (_result.Classification.Kind == DocumentKind.Markdown)
        {
            _ = NativeMethods.SetWindowText(_alternativeButton, UiText.Preview);
            _ = NativeMethods.ShowWindow(_alternativeButton, NativeMethods.ShowNormal);
            _ = NativeMethods.ShowWindow(_splitButton, NativeMethods.ShowNormal);
            _displayMode = DocumentDisplayMode.Original;
        }
        else
        {
            _ = NativeMethods.ShowWindow(_alternativeButton, NativeMethods.ShowHide);
            _ = NativeMethods.ShowWindow(_splitButton, NativeMethods.ShowHide);
        }
    }

    private void ShowSummary(string? overrideMessage = null)
    {
        string size = FormatFileSize(_result.FileSize);
        string details = UiText.DocumentSummary(_result.Classification.TypeName, size, _result.ResolvedPath);
        string message = string.IsNullOrWhiteSpace(overrideMessage) ? _result.Message : overrideMessage;
        _summaryLabel = CreateChild(
            NativeMethods.StaticClass,
            $"{message}\r\n\r\n{details}",
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.StaticLeft,
            0);
        if (_result.CanOpenExternally)
        {
            _openExternalButton = CreateButton(UiText.OpenExternally, CommandOpenExternal, false);
        }
    }

    private void DisposeContentControls()
    {
        _originalEditor?.Dispose();
        _formattedEditor?.Dispose();
        _imageView?.Dispose();
        _markdownPreviewVersion++;
        _markdownPreview?.Dispose();
        _originalEditor = null;
        _formattedEditor = null;
        _imageView = null;
        _markdownPreview = null;
        DestroyChild(ref _summaryLabel);
        DestroyChild(ref _targetLabel);
        DestroyChild(ref _openExternalButton);
        DestroyChild(ref _previewErrorLabel);
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
            _findPosition = 0;
            return;
        }

        switch (command)
        {
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
                _wordWrap = IsChecked(_wordWrapButton);
                _originalEditor?.SetWordWrap(_wordWrap);
                _formattedEditor?.SetWordWrap(_wordWrap);
                break;
            case CommandWhitespace:
                _showWhitespace = IsChecked(_whitespaceButton);
                _originalEditor?.SetWhitespaceVisible(_showWhitespace);
                _formattedEditor?.SetWhitespaceVisible(_showWhitespace);
                break;
            case CommandOpenExternal:
                ShowLaunchResult(ExternalProgramLauncher.OpenWithDefaultApplication(_result.ResolvedPath));
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
        }
    }

    private void ShowOriginal()
    {
        if (_result.Classification.Kind == DocumentKind.Markdown)
        {
            _markdownPreviewVersion++;
            _markdownPreview?.Dispose();
            _markdownPreview = null;
            DestroyChild(ref _previewErrorLabel);
            _displayMode = DocumentDisplayMode.Original;
        }

        _showAlternative = false;
        _originalEditor?.SetVisible(true);
        _formattedEditor?.SetVisible(false);
        Layout();
    }

    private void ShowAlternative()
    {
        if (_result.Classification.Kind == DocumentKind.Markdown)
        {
            _ = SetMarkdownModeAsync(DocumentDisplayMode.Preview);
            return;
        }

        if (_formattedEditor is null)
        {
            return;
        }

        _showAlternative = true;
        _originalEditor?.SetVisible(false);
        _formattedEditor.SetVisible(true);
        Layout();
    }

    private async Task SetMarkdownModeAsync(DocumentDisplayMode mode)
    {
        if (_result.Classification.Kind != DocumentKind.Markdown || _disposed)
        {
            return;
        }

        _displayMode = mode;
        _showAlternative = mode != DocumentDisplayMode.Original;
        if (mode == DocumentDisplayMode.Original)
        {
            ShowOriginal();
            return;
        }

        _originalEditor?.SetVisible(mode == DocumentDisplayMode.Split);
        int requestedVersion = ++_markdownPreviewVersion;
        _markdownPreview?.Dispose();
        _markdownPreview = null;
        DestroyChild(ref _previewErrorLabel);
        Layout();
        try
        {
            string html = await MarkdownPreviewRenderer.RenderAsync(
                _result.Text ?? string.Empty,
                _workspaceRoot,
                _result.RequestedPath,
                NativeTheme.IsDark(_settings.Theme),
                _settings.TextFontFamily,
                _settings.MonospaceFontFamily,
                _settings.FontSize);
            if (_disposed || requestedVersion != _markdownPreviewVersion || _displayMode == DocumentDisplayMode.Original)
            {
                return;
            }

            MarkdownWebViewHost preview = await MarkdownWebViewHost.CreateAsync(
                Handle,
                html,
                (path, anchor) => _openLinkedFile(path, null, anchor),
                _setStatus);
            if (_disposed || requestedVersion != _markdownPreviewVersion || _displayMode == DocumentDisplayMode.Original)
            {
                preview.Dispose();
                return;
            }

            _markdownPreview = preview;
            _markdownPreviewError = null;
            Layout();
            if (!string.IsNullOrEmpty(_pendingAnchor))
            {
                string anchor = _pendingAnchor;
                _pendingAnchor = null;
                await preview.NavigateToAnchorAsync(anchor);
            }
        }
        catch (Exception exception) when (exception is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or IOException)
        {
            if (!_disposed && requestedVersion == _markdownPreviewVersion)
            {
                if (exception is WebView2RuntimeNotFoundException)
                {
                    RuntimeDependencyPrompt.ShowWebView2Missing(Handle);
                }

                _markdownPreviewError = exception.ToString();
                _setStatus(exception.Message);
                _previewErrorLabel = CreateChild(
                    NativeMethods.StaticClass,
                    UiText.MarkdownRuntimeMissing,
                    NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.StaticCenter | NativeMethods.StaticCenterImage,
                    0);
                Layout();
            }
        }
    }

    private void ShowGoToLinePrompt()
    {
        string? input = NativeTextPrompt.Show(Handle, UiText.GoToLine, UiText.LineNumber);
        if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out int line) && line > 0)
        {
            GoToLine(line);
        }
    }

    private void FindNext(bool backwards)
    {
        string query = NativeMethods.GetWindowTextValue(_findEdit);
        if (string.IsNullOrEmpty(query) || ActiveEditor is null)
        {
            return;
        }

        string source = ActiveText;
        bool matchCase = IsChecked(_matchCaseButton);
        bool wholeWord = IsChecked(_wholeWordButton);
        bool regularExpression = IsChecked(_regularExpressionButton);
        try
        {
            (int Start, int Length)? match = FindMatch(source, query, _findPosition, backwards, matchCase, wholeWord, regularExpression);
            if (match is null)
            {
                _ = NativeMethods.SetWindowText(_findStatus, UiText.FindNotFound);
                _findPosition = backwards ? source.Length : 0;
                return;
            }

            ActiveEditor.SelectUtf8Range(match.Value.Start, match.Value.Start + match.Value.Length, source);
            _findPosition = backwards ? match.Value.Start : match.Value.Start + Math.Max(1, match.Value.Length);
            _ = NativeMethods.SetWindowText(_findStatus, UiText.FindLocated);
        }
        catch (RegexMatchTimeoutException)
        {
            _ = NativeMethods.SetWindowText(_findStatus, UiText.FindTimedOut);
        }
        catch (ArgumentException)
        {
            _ = NativeMethods.SetWindowText(_findStatus, UiText.InvalidRegularExpression);
        }
    }

    internal static (int Start, int Length)? FindMatch(
        string source,
        string query,
        int position,
        bool backwards,
        bool matchCase,
        bool wholeWord,
        bool regularExpression)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0)
        {
            return null;
        }

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

        Regex regex = new(pattern, options, TimeSpan.FromMilliseconds(250));
        int start = backwards ? Math.Clamp(position, 0, source.Length) : Math.Clamp(position, 0, source.Length);
        Match match = regex.Match(source, start);
        if (!match.Success)
        {
            match = regex.Match(source, backwards ? source.Length : 0);
        }

        return match.Success ? (match.Index, match.Length) : null;
    }

    private void SetFindBarVisible(bool visible)
    {
        foreach (nint control in new[]
        {
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

    private void Layout()
    {
        if (Handle == 0 || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = Math.Max(0, client.Right - client.Left);
        int height = Math.Max(0, client.Bottom - client.Top);
        int x = 8;
        Move(_originalButton, x, 5, 58, 26);
        x += 62;
        Move(_alternativeButton, x, 5, 64, 26);
        x += 68;
        Move(_splitButton, x, 5, 58, 26);
        x += 62;
        Move(_findButton, x, 5, 58, 26);
        x += 62;
        Move(_goToButton, x, 5, 68, 26);
        x += 72;
        Move(_wordWrapButton, x, 6, 86, 24);
        x += 90;
        Move(_whitespaceButton, x, 6, 100, 24);

        int contentTop = ToolbarHeight;
        if (_targetLabel != 0)
        {
            Move(_targetLabel, 8, contentTop + 3, Math.Max(0, width - 16), 22);
            contentTop += 26;
        }

        if (_findVisible)
        {
            int findY = ToolbarHeight;
            Move(_findEdit, 8, findY + 5, Math.Min(220, Math.Max(80, width / 4)), 26);
            int findX = 236;
            Move(_matchCaseButton, findX, findY + 6, 90, 24);
            findX += 92;
            Move(_wholeWordButton, findX, findY + 6, 82, 24);
            findX += 84;
            Move(_regularExpressionButton, findX, findY + 6, 98, 24);
            findX += 100;
            Move(_findPreviousButton, findX, findY + 5, 62, 26);
            findX += 64;
            Move(_findNextButton, findX, findY + 5, 62, 26);
            findX += 66;
            Move(_findStatus, findX, findY + 9, Math.Max(0, width - findX - 42), 20);
            Move(_findCloseButton, Math.Max(findX, width - 34), findY + 5, 28, 26);
            contentTop += FindBarHeight;
        }

        int contentHeight = Math.Max(0, height - contentTop);
        int originalWidth = _result.Classification.Kind == DocumentKind.Markdown && _displayMode == DocumentDisplayMode.Split
            ? width / 2
            : width;
        _originalEditor?.SetBounds(0, contentTop, originalWidth, contentHeight);
        _formattedEditor?.SetBounds(0, contentTop, width, contentHeight);
        if (_markdownPreview is not null)
        {
            int previewX = _displayMode == DocumentDisplayMode.Split ? originalWidth : 0;
            _markdownPreview.SetBounds(previewX, contentTop, Math.Max(0, width - previewX), contentHeight);
            _markdownPreview.SetVisible(_displayMode is DocumentDisplayMode.Preview or DocumentDisplayMode.Split);
        }

        if (_previewErrorLabel != 0)
        {
            int previewX = _displayMode == DocumentDisplayMode.Split ? originalWidth : 0;
            Move(_previewErrorLabel, previewX, contentTop, Math.Max(0, width - previewX), contentHeight);
        }
        _imageView?.SetBounds(0, contentTop, width, Math.Max(0, contentHeight - 42));
        if (_summaryLabel != 0)
        {
            Move(_summaryLabel, 24, contentTop + 24, Math.Max(0, width - 48), Math.Max(0, contentHeight - 88));
        }

        if (_openExternalButton != 0)
        {
            Move(_openExternalButton, 24, Math.Max(contentTop + 24, height - 50), 180, 28);
        }
    }

    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
    }

    private static bool IsChecked(nint button)
    {
        return NativeMethods.SendMessage(button, NativeMethods.ButtonMessageGetCheck, 0, 0) == (nint)NativeMethods.ButtonChecked;
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
