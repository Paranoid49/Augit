using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed class NativeConflictResolverDialog : IDisposable
{
    private const string WindowClassName = "Augit.ConflictResolver.Native";
    private const int CommandAcceptYours = 1;
    private const int CommandAcceptTheirs = 2;
    private const int CommandAcceptBoth = 3;
    private const int CommandPrevious = 4;
    private const int CommandNext = 5;
    private const int CommandSave = 6;
    private const int CommandClose = 7;
    private const uint WindowMessageExternalChange = NativeMethods.WindowMessageApp + 45;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeConflictResolverDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitRepositorySnapshot _repository;
    private readonly GitConflictService _service;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly string _fullPath;
    private GitConflictDocument? _document;
    private FileSystemWatcher? _watcher;
    private nint _handle;
    private nint _yoursLabel;
    private nint _resultLabel;
    private nint _theirsLabel;
    private nint _acceptYoursButton;
    private nint _acceptTheirsButton;
    private nint _acceptBothButton;
    private nint _previousButton;
    private nint _nextButton;
    private nint _saveButton;
    private nint _closeButton;
    private nint _noticeLabel;
    private ScintillaControl? _yours;
    private ScintillaControl? _result;
    private ScintillaControl? _theirs;
    private bool _resultChangedByAction;
    private bool _saving;
    private bool _closed;
    private int _blockIndex;
    private int _externalChangePending;

    internal NativeConflictResolverDialog(
        nint owner,
        GitRepositorySnapshot repository,
        GitConflictService service,
        GitConflictDocument document,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(setStatus);
        if (document.ContentKind != GitConflictContentKind.Text
            || document.ResultText is null
            || document.FileVersion is null
            || repository.RepositoryRoot is null)
        {
            throw new ArgumentException(UiText.ConflictTextUnavailable, nameof(document));
        }

        _owner = owner;
        _repository = repository;
        _service = service;
        _document = document;
        _settings = settings;
        _setStatus = setStatus;
        _fullPath = Path.GetFullPath(Path.Combine(
            repository.RepositoryRoot,
            document.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        EnsureWindowClass();
        (int x, int y) = Center(owner, 1260, 760);
        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            $"{UiText.ConflictResolver} - {document.RelativePath}",
            NativeMethods.WindowStylePopup
                | NativeMethods.WindowStyleCaption
                | NativeMethods.WindowStyleSystemMenu
                | NativeMethods.WindowStyleThickFrame
                | NativeMethods.WindowStyleClipChildren,
            x,
            y,
            1260,
            760,
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.ConflictResolverCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        CreateControls();
        ApplyDocument(document, replaceResult: true);
        ApplyAppearance();
        StartWatcher();
        Layout();
    }

    internal static bool Show(
        nint owner,
        GitRepositorySnapshot repository,
        GitConflictService service,
        GitConflictDocument document,
        ApplicationSettings settings,
        Action<string> setStatus)
    {
        using NativeConflictResolverDialog dialog = new(
            owner,
            repository,
            service,
            document,
            settings,
            setStatus);
        dialog.Run();
        return dialog._saving;
    }

    internal nint HandleForTest => _handle;

    internal bool YoursIsReadOnlyForTest => _yours?.IsReadOnly == true;

    internal bool ResultIsReadOnlyForTest => _result?.IsReadOnly != false;

    internal bool TheirsIsReadOnlyForTest => _theirs?.IsReadOnly == true;

    internal string? ResultTextForTest => _result?.GetTextContent();

    internal void CloseForTest()
    {
        Close(force: true);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
        _yours?.Dispose();
        _result?.Dispose();
        _theirs?.Dispose();
        _yours = null;
        _result = null;
        _theirs = null;
        _document = null;
        Close(force: true);
        GC.SuppressFinalize(this);
    }

    private void Run()
    {
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        try
        {
            while (!_closed && NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0) > 0)
            {
                _ = NativeMethods.TranslateMessage(ref message);
                _ = NativeMethods.DispatchMessage(ref message);
            }
        }
        finally
        {
            _ = NativeMethods.EnableWindow(_owner, true);
            _ = NativeMethods.SetForegroundWindow(_owner);
        }
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
                throw new Win32Exception(error, UiText.ConflictResolverClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeConflictResolverDialog? instance;
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
                instance.HandleCommand(NativeMethods.LowWord(wordParameter));
                return 0;
            case WindowMessageExternalChange:
                _ = instance.HandleExternalChangeAsync();
                return 0;
            case NativeMethods.WindowMessageClose:
                instance.Close();
                return 0;
            default:
                return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }
    }

    private void CreateControls()
    {
        _yoursLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft);
        _resultLabel = CreateControl(NativeMethods.StaticClass, UiText.FinalResult, 0, NativeMethods.StaticLeft);
        _theirsLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft);
        _acceptYoursButton = CreateButton(UiText.AcceptLeftBlock, CommandAcceptYours);
        _acceptTheirsButton = CreateButton(UiText.AcceptRightBlock, CommandAcceptTheirs);
        _acceptBothButton = CreateButton(UiText.AcceptBothBlock, CommandAcceptBoth);
        _previousButton = CreateButton(UiText.Previous, CommandPrevious);
        _nextButton = CreateButton(UiText.Next, CommandNext);
        _saveButton = CreateButton(UiText.SaveAndResolve, CommandSave);
        _closeButton = CreateButton(UiText.Close, CommandClose);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft);
        _yours = new(_handle, 20);
        _result = new(_handle, 21);
        _theirs = new(_handle, 22);
        _result.SetEditable(true);
    }

    private nint CreateButton(string text, int identifier)
    {
        return CreateControl(NativeMethods.ButtonClass, text, identifier, NativeMethods.ButtonPushButton);
    }

    private nint CreateControl(string className, string text, int identifier, uint specificStyle)
    {
        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleTabStop
                | specificStyle,
            0,
            0,
            0,
            0,
            _handle,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.ConflictResolverControlCreateFailed);
        }

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        return control;
    }

    private void HandleCommand(int command)
    {
        switch (command)
        {
            case CommandAcceptYours:
                AcceptBlock(GitConflictBlockChoice.Yours);
                break;
            case CommandAcceptTheirs:
                AcceptBlock(GitConflictBlockChoice.Theirs);
                break;
            case CommandAcceptBoth:
                AcceptBlock(GitConflictBlockChoice.Both);
                break;
            case CommandPrevious:
                MoveBlock(-1);
                break;
            case CommandNext:
                MoveBlock(1);
                break;
            case CommandSave:
                _ = SaveAsync();
                break;
            case CommandClose:
                Close();
                break;
        }
    }

    private void AcceptBlock(GitConflictBlockChoice choice)
    {
        if (_result is null)
        {
            return;
        }

        string text = _result.GetTextContent();
        IReadOnlyList<GitConflictBlock> blocks = GitConflictText.Parse(text);
        if (blocks.Count == 0)
        {
            UpdateBlockState(text);
            return;
        }

        _blockIndex = Math.Clamp(_blockIndex, 0, blocks.Count - 1);
        if (!GitConflictText.TryResolveBlock(text, _blockIndex, choice, out string? resolved))
        {
            return;
        }

        _result.SetTextContent(resolved!);
        _result.SetEditable(true);
        _resultChangedByAction = true;
        UpdateBlockState(resolved!);
    }

    private void MoveBlock(int direction)
    {
        if (_result is null)
        {
            return;
        }

        IReadOnlyList<GitConflictBlock> blocks = GitConflictText.Parse(_result.GetTextContent());
        if (blocks.Count == 0)
        {
            _blockIndex = 0;
            UpdateBlockState(_result.GetTextContent());
            return;
        }

        _blockIndex = (_blockIndex + direction + blocks.Count) % blocks.Count;
        _result.GoToPosition(blocks[_blockIndex].Start);
        UpdateBlockState(_result.GetTextContent());
    }

    private async Task SaveAsync()
    {
        if (_saving || _document is null || _result is null)
        {
            return;
        }

        string text = _result.GetTextContent();
        if (GitConflictText.Parse(text).Count > 0)
        {
            ShowError(UiText.UnresolvedConflictBlocks);
            return;
        }

        SetControlsEnabled(false);
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            GitConflictMutationResult saved = await _service.SaveResolvedAsync(
                _repository,
                new(
                    _document.RelativePath,
                    text,
                    _document.FileVersion!,
                    _document.Operation));
            if (!saved.IsSuccess)
            {
                ShowError(saved.ErrorMessage ?? UiText.GitUnavailable);
                StartWatcher();
                return;
            }

            _saving = true;
            _result.MarkSaved();
            _resultChangedByAction = false;
            _setStatus(UiText.ConflictResolved);
            Close(force: true);
        }
        finally
        {
            if (!_closed)
            {
                SetControlsEnabled(true);
            }
        }
    }

    private async Task HandleExternalChangeAsync()
    {
        try
        {
            await HandleExternalChangeCoreAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _externalChangePending, 0);
        }
    }

    private async Task HandleExternalChangeCoreAsync()
    {
        if (_closed || _saving || _document is null)
        {
            return;
        }

        bool keepCurrent = false;
        if (IsResultDirty)
        {
            int choice = NativeMethods.MessageBox(
                _handle,
                UiText.ExternalConflictChanged,
                UiText.AppName,
                NativeMethods.MessageBoxYesNo | NativeMethods.MessageBoxIconWarning);
            keepCurrent = choice != NativeMethods.DialogResultYes;
        }

        GitConflictLoadResult loaded = await _service.ReloadWorkingFileAsync(
            _repository,
            _document);
        if (_closed)
        {
            return;
        }

        if (!loaded.IsSuccess || loaded.Document is null)
        {
            SetNotice(loaded.ErrorMessage ?? UiText.GitUnavailable);
            return;
        }

        string? current = keepCurrent ? _result?.GetTextContent() : null;
        ApplyDocument(loaded.Document, replaceResult: !keepCurrent);
        if (keepCurrent && current is not null && _result is not null)
        {
            _result.SetTextContent(current);
            _result.SetEditable(true);
            _resultChangedByAction = true;
            UpdateBlockState(current);
            SetNotice("已保留当前未保存内容，后续保存将覆盖刚确认的外部版本。");
        }
        else
        {
            SetNotice("已重新载入外部冲突文件内容。");
        }
    }

    private void ApplyDocument(GitConflictDocument document, bool replaceResult)
    {
        _document = document;
        _ = NativeMethods.SetWindowText(_yoursLabel, document.YoursLabel);
        _ = NativeMethods.SetWindowText(_theirsLabel, document.TheirsLabel);
        _yours?.SetTextContent(document.YoursText ?? "此侧不存在。");
        _theirs?.SetTextContent(document.TheirsText ?? "此侧不存在。");
        if (replaceResult && _result is not null)
        {
            _result.SetTextContent(document.ResultText ?? string.Empty);
            _result.SetEditable(true);
            _result.MarkSaved();
            _resultChangedByAction = false;
            _blockIndex = 0;
        }

        UpdateBlockState(_result?.GetTextContent() ?? string.Empty);
    }

    private void UpdateBlockState(string text)
    {
        int count = GitConflictText.Parse(text).Count;
        if (count == 0)
        {
            _blockIndex = 0;
            SetNotice("没有剩余冲突块，可以人工检查后保存。 ");
        }
        else
        {
            _blockIndex = Math.Clamp(_blockIndex, 0, count - 1);
            SetNotice($"未处理冲突块：{count}，当前位置：{_blockIndex + 1}");
        }

        bool hasBlocks = count > 0;
        _ = NativeMethods.EnableWindow(_acceptYoursButton, hasBlocks);
        _ = NativeMethods.EnableWindow(_acceptTheirsButton, hasBlocks);
        _ = NativeMethods.EnableWindow(_acceptBothButton, hasBlocks);
        _ = NativeMethods.EnableWindow(_previousButton, hasBlocks);
        _ = NativeMethods.EnableWindow(_nextButton, hasBlocks);
    }

    private void StartWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        string? directory = Path.GetDirectoryName(_fullPath);
        string fileName = Path.GetFileName(_fullPath);
        if (directory is null || fileName.Length == 0 || !Directory.Exists(directory))
        {
            return;
        }

        _watcher = new(directory, fileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
        };
        _watcher.Changed += OnExternalFileChanged;
        _watcher.Created += OnExternalFileChanged;
        _watcher.Deleted += OnExternalFileChanged;
        _watcher.Renamed += OnExternalFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnExternalFileChanged(object sender, FileSystemEventArgs eventArgs)
    {
        if (_closed || _saving || _handle == 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _externalChangePending, 1) == 0)
        {
            _ = NativeMethods.PostMessage(_handle, WindowMessageExternalChange, 0, 0);
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        foreach (nint control in new[]
        {
            _acceptYoursButton,
            _acceptTheirsButton,
            _acceptBothButton,
            _previousButton,
            _nextButton,
            _saveButton,
            _closeButton,
        })
        {
            _ = NativeMethods.EnableWindow(control, enabled);
        }

        _result?.SetEditable(enabled);
    }

    private void ApplyAppearance()
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, dark);
        foreach (nint control in new[]
        {
            _yoursLabel,
            _resultLabel,
            _theirsLabel,
            _acceptYoursButton,
            _acceptTheirsButton,
            _acceptBothButton,
            _previousButton,
            _nextButton,
            _saveButton,
            _closeButton,
            _noticeLabel,
        })
        {
            NativeTheme.ApplyToControl(control, dark);
        }

        _yours?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark);
        _result?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark);
        _theirs?.ApplyAppearance(_settings.MonospaceFontFamily, _settings.FontSize, dark);
    }

    private void Layout()
    {
        if (_handle == 0 || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle rectangle))
        {
            return;
        }

        int width = Math.Max(0, rectangle.Right - rectangle.Left);
        int height = Math.Max(0, rectangle.Bottom - rectangle.Top);
        const int Gap = 6;
        int columnWidth = Math.Max(0, (width - 24 - (Gap * 2)) / 3);
        int left = 8;
        int middle = left + columnWidth + Gap;
        int right = middle + columnWidth + Gap;
        Move(_yoursLabel, left, 8, columnWidth, 22);
        Move(_resultLabel, middle, 8, columnWidth, 22);
        Move(_theirsLabel, right, 8, columnWidth, 22);
        int editorHeight = Math.Max(0, height - 126);
        _yours?.SetBounds(left, 32, columnWidth, editorHeight);
        _result?.SetBounds(middle, 32, columnWidth, editorHeight);
        _theirs?.SetBounds(right, 32, columnWidth, editorHeight);
        int actionTop = Math.Max(40, height - 86);
        Move(_acceptYoursButton, 8, actionTop, 112, 28);
        Move(_acceptTheirsButton, 126, actionTop, 112, 28);
        Move(_acceptBothButton, 244, actionTop, 112, 28);
        Move(_previousButton, 370, actionTop, 70, 28);
        Move(_nextButton, 446, actionTop, 70, 28);
        Move(_noticeLabel, 530, actionTop + 4, Math.Max(0, width - 810), 42);
        Move(_saveButton, Math.Max(8, width - 260), actionTop, 164, 28);
        Move(_closeButton, Math.Max(8, width - 88), actionTop, 80, 28);
    }

    private static void Move(nint window, int x, int y, int width, int height)
    {
        if (window != 0)
        {
            _ = NativeMethods.MoveWindow(window, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
    }

    private void ShowError(string message)
    {
        SetNotice(message);
        _setStatus(message);
        _ = NativeMethods.MessageBox(_handle, message, UiText.AppName, NativeMethods.MessageBoxIconWarning);
    }

    private void SetNotice(string message)
    {
        _ = NativeMethods.SetWindowText(_noticeLabel, message);
    }

    private void Close(bool force = false)
    {
        if (_closed)
        {
            return;
        }

        if (!force && IsResultDirty && NativeMethods.MessageBox(
            _handle,
            "最终结果有未保存修改，确定关闭吗？",
            UiText.AppName,
            NativeMethods.MessageBoxOkCancel | NativeMethods.MessageBoxIconWarning) != NativeMethods.DialogResultOk)
        {
            return;
        }

        _closed = true;
        _watcher?.Dispose();
        _watcher = null;
        nint handle = _handle;
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

    private bool IsResultDirty => _resultChangedByAction || _result?.IsModified == true;

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
}
