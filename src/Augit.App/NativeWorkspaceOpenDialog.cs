using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.App;

/// <summary>
/// 显示打开工作区、最近目录和克隆入口的轻量原生窗口。
/// </summary>
internal sealed class NativeWorkspaceOpenDialog : IDisposable
{
    private const string WindowClassName = "Augit.WorkspaceOpenDialog.Native";
    private const int DialogWidth = 930;
    private const int DialogHeight = 480;
    private const int CategoryIdentifier = 10;
    private const int RecentIdentifier = 11;
    private const int CommandOpen = 12;
    private const int CommandCancel = 13;
    private const int CommandHeaderClose = 14;
    private const int HeaderHeight = 45;
    private const int FooterHeight = 53;
    private const uint DialogStyle = NativeMethods.WindowStylePopup
        | NativeMethods.WindowStyleClipChildren;
    private const string SelectDirectoryLabel = "选择目录…";
    private const string CloneRepositoryLabel = "克隆仓库…";
    private const string OpenLabel = "打开";
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeWorkspaceOpenDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly ApplicationSettings _settings;
    private readonly Action<string> _openWorkspace;
    private readonly Action _clone;
    private readonly Action _closedCallback;
    private readonly string[] _recentDirectories;
    private nint _handle;
    private nint _categoryList;
    private nint _recentList;
    private nint _detailTitle;
    private nint _detailPath;
    private nint _detailHint;
    private nint _noticeLabel;
    private nint _openButton;
    private nint _cancelButton;
    private nint _headerCloseButton;
    private nint _controlBrush;
    private NativeToolTip? _toolTip;
    private NativeModalScrim? _scrim;
    private readonly NativeModalFocusScope _focusScope;
    private bool _dark;
    private bool _closed;
    private bool _disposed;

    private NativeWorkspaceOpenDialog(
        nint owner,
        ApplicationSettings settings,
        Action<string> openWorkspace,
        Action clone,
        Action closed)
    {
        _owner = owner;
        _focusScope = new(owner);
        _settings = settings;
        _openWorkspace = openWorkspace;
        _clone = clone;
        _closedCallback = closed;
        _recentDirectories = settings.RecentWorkspaces
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
        EnsureWindowClass();
        (int x, int y) = Center(owner, S(DialogWidth), S(DialogHeight));
        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.OpenWorkspace,
            DialogStyle,
            x,
            y,
            S(DialogWidth),
            S(DialogHeight),
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "打开工作区窗口创建失败。");
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        CreateControls();
        ApplyAppearance();
        PopulateRecentDirectories();
        Layout();
    }

    internal static (int Width, int Height, int HeaderHeight, int FooterHeight) LogicalLayoutForTest =>
        (DialogWidth, DialogHeight, HeaderHeight, FooterHeight);

    internal static uint WindowStyleForTest => DialogStyle;

    internal static string TitleForTest => UiText.OpenWorkspace;

    internal static bool CategoryShowsRecentListForTest(int category) => category == 0;

    internal static IReadOnlyList<string> CategoryLabelsForTest =>
        [UiText.RecentWorkspaces, SelectDirectoryLabel, CloneRepositoryLabel];

    internal static IReadOnlyList<int> TabOrderForTest =>
        [CategoryIdentifier, RecentIdentifier, CommandCancel, CommandOpen, CommandHeaderClose];

    internal static IReadOnlyList<string> RecentDirectoriesForTest(ApplicationSettings settings) =>
        settings.RecentWorkspaces
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

    internal static NativeWorkspaceOpenDialog Show(
        nint owner,
        ApplicationSettings settings,
        Action<string> openWorkspace,
        Action clone,
        Action closed)
    {
        NativeWorkspaceOpenDialog dialog = new(owner, settings, openWorkspace, clone, closed);
        dialog._scrim = NativeModalScrim.Begin(owner, dialog._dark);
        _ = NativeMethods.EnableWindow(owner, false);
        _ = NativeMethods.ShowWindow(dialog._handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(dialog._handle);
        _ = NativeMethods.SetForegroundWindow(dialog._handle);
        _ = NativeMethods.SetFocus(dialog._recentList != 0 ? dialog._recentList : dialog._categoryList);
        return dialog;
    }

    internal void FocusForTest()
    {
        if (_closed || _handle == 0)
        {
            return;
        }

        _ = NativeMethods.SetForegroundWindow(_handle);
        _ = NativeMethods.SetFocus(_recentList != 0 ? _recentList : _categoryList);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Close(invokeCallback: false);
        GC.SuppressFinalize(this);
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeWorkspaceOpenDialog? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        if (message is NativeMethods.WindowMessageControlColorButton
            or NativeMethods.WindowMessageControlColorStatic
            or NativeMethods.WindowMessageControlColorEdit
            or NativeMethods.WindowMessageControlColorListBox)
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
            NativeMethods.WindowMessageCommand => instance.CommandMessage(wordParameter),
            NativeMethods.WindowMessageKeyDown when wordParameter == NativeMethods.VirtualKeyTab =>
                instance.MoveFocus(NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0),
            NativeMethods.WindowMessageKeyDown when wordParameter == NativeMethods.VirtualKeyEscape => instance.CloseMessage(),
            NativeMethods.WindowMessageClose => instance.CloseMessage(),
            _ => NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter),
        };
    }

    /// <summary>
    /// 按打开工作区视觉稿在分类、列表和底部动作之间循环移动焦点。
    /// </summary>
    private nint MoveFocus(bool backwards)
    {
        NativeFocusNavigation.MoveWithinRegion(
            [_categoryList, _recentList, _cancelButton, _openButton, _headerCloseButton],
            NativeMethods.GetFocus(),
            backwards);
        return 0;
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
            NativeThemePalette palette = NativeTheme.Palette(_dark);
            NativeMethods.Rectangle client = new();
            _ = NativeMethods.GetClientRectangle(_handle, out client);
            Fill(deviceContext, client, palette.Panel);
            NativeMethods.Rectangle header = client;
            header.Bottom = Math.Min(header.Bottom, S(HeaderHeight));
            Fill(deviceContext, header, palette.Chrome);
            NativeMethods.Rectangle footer = client;
            footer.Top = Math.Max(footer.Top, footer.Bottom - S(FooterHeight));
            Fill(deviceContext, footer, palette.PanelMuted);
            NativeMethods.Rectangle divider = footer;
            divider.Bottom = divider.Top + Math.Max(1, S(1));
            Fill(deviceContext, divider, palette.Border);
            NativeMethods.Rectangle title = new()
            {
                Left = S(22),
                Top = S(12),
                Right = Math.Max(S(22), client.Right - S(100)),
                Bottom = S(35),
            };
            DrawText(deviceContext, UiText.OpenWorkspace, title, palette.Text, NativeTheme.UiMediumFont);
        }
        finally
        {
            _ = NativeMethods.EndPaint(_handle, ref paint);
        }

        return 0;
    }

    private bool DrawControl(nint parameter)
    {
        if (parameter == 0)
        {
            return false;
        }

        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        if (item.ControlIdentifier is CategoryIdentifier or RecentIdentifier)
        {
            return DrawListItem(item);
        }

        if (item.ControlIdentifier == CommandHeaderClose)
        {
            return DrawHeaderCloseButton(item);
        }

        if (item.ControlIdentifier is CommandOpen or CommandCancel)
        {
            return NativeTheme.DrawFlatButton(
                parameter,
                _dark,
                emphasized: item.ControlIdentifier == CommandOpen,
                outlined: item.ControlIdentifier == CommandCancel);
        }

        return false;
    }

    private bool DrawListItem(NativeMethods.DrawItem item)
    {
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        bool selected = (item.ItemState & NativeMethods.OwnerDrawSelected) != 0;
        Fill(item.DeviceContext, item.ItemRectangle, selected ? palette.AccentSoft : palette.Panel);

        int index = unchecked((int)item.ItemIdentifier);
        string label;
        int glyphKind;
        if (item.ControlIdentifier == CategoryIdentifier)
        {
            string[] categories = [UiText.RecentWorkspaces, SelectDirectoryLabel, CloneRepositoryLabel];
            label = index >= 0 && index < categories.Length ? categories[index] : string.Empty;
            glyphKind = index switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                _ => -1,
            };
        }
        else
        {
            label = index >= 0 && index < _recentDirectories.Length
                ? _recentDirectories[index]
                : UiText.NoRecentWorkspaces;
            glyphKind = 3;
        }

        int centerX = item.ItemRectangle.Left + S(19);
        int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
        DrawWorkspaceGlyph(item.DeviceContext, glyphKind, centerX, centerY, selected ? palette.Accent : palette.Muted);

        NativeMethods.Rectangle textRectangle = item.ItemRectangle;
        textRectangle.Left += S(38);
        textRectangle.Right -= S(8);
        if (item.ControlIdentifier == RecentIdentifier && index >= 0 && index < _recentDirectories.Length)
        {
            string path = _recentDirectories[index];
            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
            {
                name = path;
            }

            NativeMethods.Rectangle nameRectangle = textRectangle;
            nameRectangle.Right -= S(190);
            DrawText(item.DeviceContext, name, nameRectangle, palette.Text, NativeTheme.UiFont);
            NativeMethods.Rectangle pathRectangle = textRectangle;
            pathRectangle.Left = Math.Max(pathRectangle.Left, pathRectangle.Right - S(190));
            DrawTextRight(item.DeviceContext, path, pathRectangle, palette.Muted, NativeTheme.UiFont);
        }
        else
        {
            DrawText(item.DeviceContext, label, textRectangle, palette.Text, NativeTheme.UiFont);
        }

        return true;
    }

    private bool DrawHeaderCloseButton(NativeMethods.DrawItem item)
    {
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        bool disabled = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0;
        bool hot = (item.ItemState & (NativeMethods.OwnerDrawHotLight | NativeMethods.OwnerDrawSelected)) != 0;
        Fill(item.DeviceContext, item.ItemRectangle, hot ? palette.Hover : palette.Chrome);
        uint color = disabled ? palette.Faint : palette.Muted;
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, Math.Max(1, S(1)), color);
        if (pen == 0)
        {
            return true;
        }

        nint previousPen = NativeMethods.SelectObject(item.DeviceContext, pen);
        int centerX = (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2;
        int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
        _ = NativeMethods.MoveTo(item.DeviceContext, centerX - S(4), centerY - S(4), 0);
        _ = NativeMethods.LineTo(item.DeviceContext, centerX + S(4), centerY + S(4));
        _ = NativeMethods.MoveTo(item.DeviceContext, centerX + S(4), centerY - S(4), 0);
        _ = NativeMethods.LineTo(item.DeviceContext, centerX - S(4), centerY + S(4));
        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(item.DeviceContext, previousPen);
        }

        _ = NativeMethods.DeleteObject(pen);
        return true;
    }

    private static void DrawWorkspaceGlyph(nint deviceContext, int kind, int centerX, int centerY, uint color)
    {
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, Math.Max(1, S(1)), color);
        if (pen == 0)
        {
            return;
        }

        nint previousPen = NativeMethods.SelectObject(deviceContext, pen);
        nint previousBrush = NativeMethods.SelectObject(deviceContext, NativeMethods.GetStockObject(NativeMethods.NullBrush));
        switch (kind)
        {
            case 0:
                _ = NativeMethods.DrawEllipse(deviceContext, centerX - S(6), centerY - S(6), centerX + S(6), centerY + S(6));
                _ = NativeMethods.MoveTo(deviceContext, centerX, centerY, 0);
                _ = NativeMethods.LineTo(deviceContext, centerX, centerY - S(4));
                _ = NativeMethods.MoveTo(deviceContext, centerX, centerY, 0);
                _ = NativeMethods.LineTo(deviceContext, centerX + S(3), centerY + S(2));
                break;
            case 1:
                _ = NativeMethods.MoveTo(deviceContext, centerX - S(7), centerY - S(4), 0);
                _ = NativeMethods.LineTo(deviceContext, centerX - S(1), centerY - S(4));
                _ = NativeMethods.LineTo(deviceContext, centerX + S(1), centerY - S(2));
                _ = NativeMethods.LineTo(deviceContext, centerX + S(7), centerY - S(2));
                _ = NativeMethods.LineTo(deviceContext, centerX + S(7), centerY + S(6));
                _ = NativeMethods.LineTo(deviceContext, centerX - S(7), centerY + S(6));
                _ = NativeMethods.LineTo(deviceContext, centerX - S(7), centerY - S(4));
                break;
            case 2:
                _ = NativeTheme.DrawCloneIcon(deviceContext, centerX, centerY, color);
                break;
            case 3:
                _ = NativeMethods.MoveTo(deviceContext, centerX - S(7), centerY - S(4), 0);
                _ = NativeMethods.LineTo(deviceContext, centerX - S(1), centerY - S(4));
                _ = NativeMethods.LineTo(deviceContext, centerX + S(1), centerY - S(2));
                _ = NativeMethods.LineTo(deviceContext, centerX + S(7), centerY - S(2));
                _ = NativeMethods.LineTo(deviceContext, centerX + S(7), centerY + S(6));
                _ = NativeMethods.LineTo(deviceContext, centerX - S(7), centerY + S(6));
                _ = NativeMethods.LineTo(deviceContext, centerX - S(7), centerY - S(4));
                _ = NativeMethods.MoveTo(deviceContext, centerX - S(1), centerY, 0);
                _ = NativeMethods.LineTo(deviceContext, centerX + S(4), centerY);
                _ = NativeMethods.DrawEllipse(deviceContext, centerX - S(3), centerY - S(2), centerX - S(1), centerY + S(2));
                _ = NativeMethods.DrawEllipse(deviceContext, centerX + S(3), centerY - S(2), centerX + S(5), centerY + S(2));
                break;
        }

        if (previousBrush != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousBrush);
        }

        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
        }

        _ = NativeMethods.DeleteObject(pen);
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
        return _controlBrush;
    }

    private void CreateControls()
    {
        _categoryList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            CategoryIdentifier,
            NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxOwnerDrawFixed
                | NativeMethods.ListBoxHasStrings
                | NativeMethods.ListBoxNoIntegralHeight);
        _recentList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            RecentIdentifier,
            NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxOwnerDrawFixed
                | NativeMethods.ListBoxHasStrings
                | NativeMethods.ListBoxNoIntegralHeight);
        _ = NativeMethods.SendMessage(_categoryList, NativeMethods.ListBoxSetItemHeight, 0, S(34));
        _ = NativeMethods.SendMessage(_recentList, NativeMethods.ListBoxSetItemHeight, 0, S(34));
        _detailTitle = CreateControl(NativeMethods.StaticClass, UiText.RecentWorkspaces, 0, NativeMethods.StaticLeft);
        _detailPath = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft);
        _detailHint = CreateControl(
            NativeMethods.StaticClass,
            "选择一个最近目录，或从左侧选择其他打开方式。",
            0,
            NativeMethods.StaticLeft);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft);
        _cancelButton = CreateControl(NativeMethods.ButtonClass, UiText.Cancel, CommandCancel, NativeMethods.ButtonOwnerDraw);
        _openButton = CreateControl(NativeMethods.ButtonClass, OpenLabel, CommandOpen, NativeMethods.ButtonOwnerDraw);
        _headerCloseButton = CreateControl(NativeMethods.ButtonClass, UiText.CloseSymbol, CommandHeaderClose, NativeMethods.ButtonOwnerDraw);
        _toolTip = new NativeToolTip(_handle);
        _toolTip.Add(_openButton, OpenLabel);
        _toolTip.Add(_cancelButton, UiText.Cancel);
        _toolTip.Add(_headerCloseButton, UiText.Close);
    }

    private nint CreateControl(string className, string text, int identifier, uint style)
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
            _handle,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "打开工作区控件创建失败。");
        }

        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        return control;
    }

    private void PopulateRecentDirectories()
    {
        foreach (string label in new[] { UiText.RecentWorkspaces, SelectDirectoryLabel, CloneRepositoryLabel })
        {
            _ = NativeMethods.SendMessage(_categoryList, NativeMethods.ListBoxAddString, 0, label);
        }

        _ = NativeMethods.SendMessage(
            _categoryList,
            NativeMethods.ListBoxSetCurrentSelection,
            0,
            0);

        foreach (string path in _recentDirectories)
        {
            _ = NativeMethods.SendMessage(_recentList, NativeMethods.ListBoxAddString, 0, path);
        }

        if (_recentDirectories.Length > 0)
        {
            _ = NativeMethods.SendMessage(_recentList, NativeMethods.ListBoxSetCurrentSelection, 0, 0);
            UpdateRecentDetail(0);
        }
        else
        {
            _ = NativeMethods.SendMessage(_recentList, NativeMethods.ListBoxAddString, 0, UiText.NoRecentWorkspaces);
            _ = NativeMethods.EnableWindow(_openButton, false);
            _ = NativeMethods.SetWindowText(_detailPath, UiText.NoRecentWorkspaces);
        }
    }

    private nint CommandMessage(nuint wordParameter)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (command == CategoryIdentifier && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            UpdateCategoryDetail();
        }
        else if (command == RecentIdentifier && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            int index = GetSelectedIndex(_recentList);
            UpdateRecentDetail(index);
        }
        else if (command == CommandOpen)
        {
            OpenSelected();
        }
        else if (command is CommandCancel or CommandHeaderClose)
        {
            CloseMessage();
        }

        return 0;
    }

    private void UpdateCategoryDetail()
    {
        int category = GetSelectedIndex(_categoryList);
        switch (category)
        {
            case 1:
                _ = NativeMethods.SetWindowText(_detailTitle, UiText.OpenFolder);
                _ = NativeMethods.SetWindowText(_detailPath, "选择一个本机固定磁盘目录作为工作区。");
                _ = NativeMethods.SetWindowText(_detailHint, "打开后项目树会按需读取目录内容，Git 状态在可用时显示。");
                _ = NativeMethods.EnableWindow(_openButton, true);
                break;
            case 2:
                _ = NativeMethods.SetWindowText(_detailTitle, UiText.CloneRepository);
                _ = NativeMethods.SetWindowText(_detailPath, "从 Git 仓库 URL 克隆到本机目录。");
                _ = NativeMethods.SetWindowText(_detailHint, "克隆窗口会继续填写仓库地址、目标目录和可选浅克隆深度。");
                _ = NativeMethods.EnableWindow(_openButton, true);
                break;
            default:
                _ = NativeMethods.SetWindowText(_detailTitle, UiText.RecentWorkspaces);
                UpdateRecentDetail(GetSelectedIndex(_recentList));
                break;
        }

        Layout();

        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    private void UpdateRecentDetail(int index)
    {
        if (GetSelectedIndex(_categoryList) != 0)
        {
            return;
        }

        if (index < 0 || index >= _recentDirectories.Length)
        {
            _ = NativeMethods.SetWindowText(_detailPath, UiText.NoRecentWorkspaces);
            _ = NativeMethods.EnableWindow(_openButton, false);
            return;
        }

        _ = NativeMethods.SetWindowText(_detailTitle, UiText.RecentWorkspaces);
        _ = NativeMethods.SetWindowText(_detailPath, _recentDirectories[index]);
        _ = NativeMethods.SetWindowText(_detailHint, UiText.RecentWorkspaceHint);
        _ = NativeMethods.EnableWindow(_openButton, true);
    }

    private void OpenSelected()
    {
        int category = GetSelectedIndex(_categoryList);
        try
        {
            if (category == 1)
            {
                string? selectedPath = NativeFolderDialog.SelectFolder(_handle);
                if (selectedPath is not null)
                {
                    CloseThen(() => _openWorkspace(selectedPath));
                }

                return;
            }

            if (category == 2)
            {
                CloseThen(_clone);
                return;
            }

            int index = GetSelectedIndex(_recentList);
            if (index < 0 || index >= _recentDirectories.Length)
            {
                _ = NativeMethods.SetWindowText(_noticeLabel, UiText.NoRecentWorkspaces);
                return;
            }

            string recentPath = _recentDirectories[index];
            if (!Directory.Exists(recentPath))
            {
                _ = NativeMethods.SetWindowText(_noticeLabel, "最近目录不存在，请选择其他目录。");
                return;
            }

            CloseThen(() => _openWorkspace(recentPath));
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or IOException)
        {
            if (_handle != 0)
            {
                _ = NativeMethods.SetWindowText(_noticeLabel, exception.Message);
            }
        }
    }

    private void CloseThen(Action action)
    {
        Close(invokeCallback: false);
        _closedCallback();
        action();
    }

    private nint CloseMessage()
    {
        Close(invokeCallback: true);
        return 0;
    }

    private void Close(bool invokeCallback)
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

        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }

        _scrim?.Dispose();
        _scrim = null;

        _toolTip?.Dispose();
        _toolTip = null;
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }

        _ = NativeMethods.EnableWindow(_owner, true);
        _ = NativeMethods.SetForegroundWindow(_owner);
        _focusScope.Restore();
        if (invokeCallback)
        {
            _closedCallback();
        }
    }

    private nint LayoutMessage()
    {
        Layout();
        return 0;
    }

    private nint HitTest()
    {
        if (!NativeMethods.GetCursorPosition(out NativeMethods.Point point)
            || !NativeMethods.ScreenToClient(_handle, ref point)
            || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return NativeMethods.HitTestClient;
        }

        return point.Y < S(HeaderHeight) && point.X < client.Right - S(54)
            ? NativeMethods.HitTestCaption
            : NativeMethods.HitTestClient;
    }

    private void Layout()
    {
        if (_handle == 0 || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = Math.Max(0, client.Right - client.Left);
        int height = Math.Max(0, client.Bottom - client.Top);
        int inset = S(17);
        int contentTop = S(HeaderHeight + 15);
        int footerTop = Math.Max(contentTop, height - S(FooterHeight));
        int managementHeight = Math.Max(S(180), Math.Min(S(350), footerTop - contentTop - S(15)));
        int categoryWidth = S(260);
        int detailLeft = inset + categoryWidth;
        int detailWidth = Math.Max(S(240), width - detailLeft - inset);
        bool showRecent = CategoryShowsRecentListForTest(GetSelectedIndex(_categoryList));
        int recentItemCount = Math.Max(1, _recentDirectories.Length);
        int recentHeight = Math.Min(S(210), recentItemCount * S(34));
        _ = NativeMethods.ShowWindow(_recentList, showRecent ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        Move(_categoryList, inset + S(8), contentTop + S(8), categoryWidth - S(16), managementHeight - S(16));
        Move(_recentList, detailLeft + S(18), contentTop + S(57), detailWidth - S(36), recentHeight);
        Move(_detailTitle, detailLeft + S(18), contentTop + S(12), detailWidth - S(36), S(24));
        _ = NativeMethods.ShowWindow(_detailPath, showRecent ? NativeMethods.ShowHide : NativeMethods.ShowNormal);
        int detailPathTop = contentTop + S(48);
        Move(_detailPath, detailLeft + S(18), detailPathTop, detailWidth - S(36), S(42));
        int detailHintTop = showRecent
            ? contentTop + S(57) + recentHeight + S(12)
            : detailPathTop + S(40);
        Move(_detailHint, detailLeft + S(18), detailHintTop, detailWidth - S(36), S(52));
        Move(_noticeLabel, inset, footerTop - S(31), Math.Max(S(120), width - inset * 2 - S(250)), S(24));
        int buttonTop = footerTop + S(13);
        Move(_cancelButton, Math.Max(inset, width - S(210)), buttonTop, S(88), S(30));
        Move(_openButton, Math.Max(inset, width - S(112)), buttonTop, S(96), S(30));
        Move(_headerCloseButton, Math.Max(inset, width - S(36)), S(8), S(28), S(28));
    }

    private void ApplyAppearance()
    {
        _dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, _dark);
        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(_dark).Panel);
        foreach (nint control in new[]
        {
            _categoryList,
            _recentList,
            _detailTitle,
            _detailPath,
            _detailHint,
            _noticeLabel,
            _openButton,
            _cancelButton,
            _headerCloseButton,
        })
        {
            NativeTheme.ApplyToControl(control, _dark);
        }

        _toolTip?.ApplyAppearance(_dark);
    }

    private static int GetSelectedIndex(nint list)
    {
        if (list == 0)
        {
            return -1;
        }

        return checked((int)NativeMethods.SendMessage(list, NativeMethods.ListBoxGetCurrentSelection, 0, 0));
    }

    private static void Move(nint control, int x, int y, int width, int height)
    {
        if (control != 0)
        {
            _ = NativeMethods.MoveWindow(control, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
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
        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(deviceContext, color);
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            text.Length,
            ref rectangle,
            NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextNoPrefix
                | NativeMethods.DrawTextEndEllipsis);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
    }

    private static void DrawTextRight(nint deviceContext, string text, NativeMethods.Rectangle rectangle, uint color, nint font)
    {
        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(deviceContext, color);
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            text.Length,
            ref rectangle,
            NativeMethods.DrawTextRight
                | NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextNoPrefix
                | NativeMethods.DrawTextEndEllipsis);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
    }

    private static (int X, int Y) Center(nint owner, int width, int height)
    {
        if (NativeMethods.GetWindowRectangle(owner, out NativeMethods.Rectangle rectangle))
        {
            return (
                rectangle.Left + Math.Max(0, (rectangle.Right - rectangle.Left - width) / 2),
                rectangle.Top + Math.Max(0, (rectangle.Bottom - rectangle.Top - height) / 2));
        }

        return (NativeMethods.UseDefault, NativeMethods.UseDefault);
    }

    private static int S(int logicalPixels) => NativeTheme.Scale(logicalPixels);

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
                throw new Win32Exception(error, "打开工作区窗口类注册失败。");
            }

            _classRegistered = true;
        }
    }
}
