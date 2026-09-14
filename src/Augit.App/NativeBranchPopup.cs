using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal enum NativeBranchPopupCommand
{
    UpdateProject,
    Commit,
    Push,
    CreateBranch,
    CheckoutRevision,
    CheckoutBranch,
    CreateTrackingBranch,
    CreateBranchFromReference,
    CompareWithWorkspace,
    CreateWorktree,
    PushReference,
    RenameReference,
    DeleteReference,
}

internal sealed record NativeBranchPopupRequest(
    NativeBranchPopupCommand Command,
    GitBranchInfo? Branch = null,
    GitTagInfo? Tag = null)
{
    internal string? ReferenceName => Branch?.Name ?? Tag?.Name;
}

internal enum NativeBranchPopupRowKind
{
    Action,
    Group,
    Branch,
    Tag,
    Empty,
}

internal sealed record NativeBranchPopupRow(
    NativeBranchPopupRowKind Kind,
    string Label,
    string Glyph,
    NativeBranchPopupRequest? Request = null,
    GitBranchInfo? Branch = null,
    GitTagInfo? Tag = null)
{
    internal bool IsReference => Kind is NativeBranchPopupRowKind.Branch or NativeBranchPopupRowKind.Tag;

    internal bool IsSelectable => Kind is NativeBranchPopupRowKind.Action
        or NativeBranchPopupRowKind.Branch
        or NativeBranchPopupRowKind.Tag;
}

internal sealed class NativeBranchPopup : IDisposable
{
    private const string WindowClassName = "Augit.BranchPopup.Native";
    private const int MainPopupWidth = 338;
    private const int MainPopupMinHeight = 176;
    private const int MainPopupMaxHeight = 306;
    private const int ActionPopupWidth = 280;
    private const int ActionPopupMinHeight = 76;
    private const int ActionPopupMaxHeight = 306;
    private const int PopupPadding = 8;
    private const int SearchHeight = 30;
    private const int ListTop = 46;
    private const int RowHeight = 30;
    private const int SearchIdentifier = 1;
    private const int ReferencesIdentifier = 2;
    private const int ActionsIdentifier = 3;
    private const int EditNotificationChanged = 0x0300;
    private const nuint SearchSubclassIdentifier = 1;
    private const nuint ReferencesSubclassIdentifier = 2;
    private const nuint ActionsSubclassIdentifier = 3;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeBranchPopup> Instances = [];
    private static readonly Dictionary<nint, NativeBranchPopup> ControlInstances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static readonly NativeMethods.SubclassProcedure ControlProcedure = HandleControlMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitReferenceSnapshot _snapshot;
    private readonly ApplicationSettings _settings;
    private readonly Func<NativeBranchPopupRequest, Task> _execute;
    private readonly Action _closed;
    private readonly List<NativeBranchPopupRow> _rows = [];
    private readonly List<NativeBranchPopupRow> _actions = [];
    private nint _handle;
    private nint _actionsHandle;
    private nint _searchEdit;
    private nint _referencesList;
    private nint _actionsList;
    private nint _controlBrush;
    private bool _disposed;

    internal NativeBranchPopup(
        nint owner,
        int anchorX,
        int anchorY,
        GitReferenceSnapshot snapshot,
        ApplicationSettings settings,
        Func<NativeBranchPopupRequest, Task> execute,
        Action closed)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(closed);
        _owner = owner;
        _snapshot = snapshot;
        _settings = settings;
        _execute = execute;
        _closed = closed;
        EnsureWindowClass();

        int width = S(MainPopupWidth);
        int height = S(MainPopupMaxHeight);
        (int x, int y) = ConstrainToWorkArea(owner, anchorX, anchorY, width, height);
        _handle = NativeMethods.CreateWindow(
            NativeMethods.WindowExtendedStyleToolWindow,
            WindowClassName,
            string.Empty,
            NativeMethods.WindowStylePopup
                | NativeMethods.WindowStyleClipChildren
                | NativeMethods.WindowStyleClipSiblings,
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InterfaceControlCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        _actionsHandle = NativeMethods.CreateWindow(
            NativeMethods.WindowExtendedStyleToolWindow,
            WindowClassName,
            string.Empty,
            NativeMethods.WindowStylePopup
                | NativeMethods.WindowStyleClipChildren
                | NativeMethods.WindowStyleClipSiblings,
            x + width + S(6),
            y,
            S(ActionPopupWidth),
            S(ActionPopupMaxHeight),
            _handle,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_actionsHandle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InterfaceControlCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_actionsHandle, this);
        }

        CreateControls();
        ApplyAppearance();
        PopulateRows(string.Empty);
    }

    internal nint Handle => _handle;

    internal nint ActionsHandleForTest => _actionsHandle;

    internal bool ActionsPopupVisibleForTest => _actionsHandle != 0
        && NativeMethods.IsWindowVisible(_actionsHandle);

    internal static int MainPopupWidthForTest => S(MainPopupWidth);

    internal static int CalculateMainPopupHeightForTest(int rowCount)
    {
        return S(CalculatePopupHeight(rowCount, MainPopupMinHeight, MainPopupMaxHeight));
    }

    internal static int CalculateActionPopupHeightForTest(int actionCount)
    {
        return S(CalculatePopupHeight(actionCount, ActionPopupMinHeight, ActionPopupMaxHeight, PopupPadding));
    }

    internal bool SearchHasFocusForTest => NativeMethods.GetFocus() == _searchEdit;

    internal bool ReferencesListHasFocusForTest => NativeMethods.GetFocus() == _referencesList;

    internal bool ActionsListHasFocusForTest => NativeMethods.GetFocus() == _actionsList;

    internal bool ActionsListVisibleForTest => NativeMethods.IsWindowVisible(_actionsList);

    internal bool ContainsWindow(nint window)
    {
        return NativeFocusNavigation.ContainsWindow(_handle, window)
            || NativeFocusNavigation.ContainsWindow(_actionsHandle, window);
    }

    internal bool HandleTabNavigation(bool backwards)
    {
        EnsureActionsVisibleForKeyboard();
        nint[] controls = [_searchEdit, _referencesList, _actionsList];
        nint[] focusable = controls
            .Where(control => control != 0
                && NativeMethods.IsWindow(control)
                && NativeMethods.IsWindowVisible(control)
                && NativeMethods.IsWindowEnabled(control))
            .ToArray();
        if (focusable.Length == 0)
        {
            return false;
        }

        nint currentFocus = NativeMethods.GetFocus();
        int currentIndex = Array.FindIndex(
            focusable,
            control => control == currentFocus
                || (currentFocus != 0 && NativeMethods.IsChild(control, currentFocus)));
        int nextIndex = currentIndex < 0
            ? (backwards ? focusable.Length - 1 : 0)
            : backwards
                ? (currentIndex + focusable.Length - 1) % focusable.Length
                : (currentIndex + 1) % focusable.Length;
        nint next = focusable[nextIndex];
        _ = NativeMethods.SetFocus(next);
        if (next != _actionsList && _actions.Count > 0)
        {
            int selectedReference = GetSelection(_referencesList);
            if (selectedReference >= 0)
            {
                PositionActions(selectedReference);
                _ = NativeMethods.ShowWindow(_actionsHandle, NativeMethods.ShowWithoutActivate);
                _ = NativeMethods.ShowWindow(_actionsList, NativeMethods.ShowNormal);
            }
        }

        return NativeMethods.GetFocus() == next;
    }

    private void EnsureActionsVisibleForKeyboard()
    {
        if (_actions.Count == 0 || _actionsHandle == 0 || _actionsList == 0)
        {
            return;
        }

        int selectedReference = GetSelection(_referencesList);
        if (selectedReference >= 0)
        {
            PositionActions(selectedReference);
            _ = NativeMethods.ShowWindow(_actionsHandle, NativeMethods.ShowWithoutActivate);
            _ = NativeMethods.ShowWindow(_actionsList, NativeMethods.ShowNormal);
        }
    }

    internal IReadOnlyList<string> RowLabelsForTest => _rows.Select(row => row.Label).ToArray();

    internal IReadOnlyList<string> ActionLabelsForTest => _actions.Select(row => row.Label).ToArray();

    internal static IReadOnlyList<NativeBranchPopupRow> BuildRowsForTest(
        GitReferenceSnapshot snapshot,
        string query)
    {
        return BuildRows(snapshot, query);
    }

    internal static IReadOnlyList<NativeBranchPopupRow> BuildActionsForTest(
        NativeBranchPopupRow row)
    {
        return BuildActions(row);
    }

    internal void Show()
    {
        if (_disposed || _handle == 0)
        {
            return;
        }

        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        _ = NativeMethods.SetForegroundWindow(_handle);
        _ = NativeMethods.SetFocus(_searchEdit);
        int currentBranchIndex = _rows.FindIndex(row => row.Branch?.IsCurrent == true);
        if (currentBranchIndex >= 0)
        {
            _ = NativeMethods.SendMessage(
                _referencesList,
                NativeMethods.ListBoxSetCurrentSelection,
                unchecked((nuint)currentBranchIndex),
                0);
            UpdateActions();
            _ = NativeMethods.SetFocus(_searchEdit);
        }
    }

    internal void SetQueryForTest(string query)
    {
        _ = NativeMethods.SetWindowText(_searchEdit, query);
        PopulateRows(query);
    }

    internal bool SelectReferenceForTest(string name)
    {
        int index = _rows.FindIndex(
            row => row.IsReference && row.Label.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        _ = NativeMethods.SendMessage(
            _referencesList,
            NativeMethods.ListBoxSetCurrentSelection,
            unchecked((nuint)index),
            0);
        UpdateActions();
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RemoveControlSubclasses();
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }

        nint handle = _handle;
        _handle = 0;
        lock (InstancesGate)
        {
            Instances.Remove(handle);
            Instances.Remove(_actionsHandle);
        }

        if (_actionsHandle != 0 && NativeMethods.IsWindow(_actionsHandle))
        {
            _ = NativeMethods.DestroyWindow(_actionsHandle);
        }

        _actionsHandle = 0;
        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }

        _closed();
        GC.SuppressFinalize(this);
    }

    private static List<NativeBranchPopupRow> BuildRows(
        GitReferenceSnapshot snapshot,
        string query)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string normalized = query.Trim();
        bool Matches(string value) => normalized.Length == 0
            || value.Contains(normalized, StringComparison.OrdinalIgnoreCase);

        List<NativeBranchPopupRow> rows = [];
        AddAction(UiText.UpdateProject, "update", NativeBranchPopupCommand.UpdateProject);
        AddAction(UiText.CommitEllipsis, "commit", NativeBranchPopupCommand.Commit);
        AddAction(UiText.PushEllipsis, "push", NativeBranchPopupCommand.Push);
        AddAction(UiText.CreateBranchEllipsis, "add", NativeBranchPopupCommand.CreateBranch);
        AddAction(UiText.CheckoutTagOrRevision, "checkout", NativeBranchPopupCommand.CheckoutRevision);

        AddBranchGroup(
            UiText.LocalReferences,
            snapshot.Branches
                .Where(branch => !branch.IsRemote && Matches(branch.Name))
                .OrderByDescending(branch => branch.IsCurrent)
                .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase));
        AddBranchGroup(
            UiText.RemoteReferences,
            snapshot.Branches
                .Where(branch => branch.IsRemote && Matches(branch.Name))
                .OrderBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase));
        GitTagInfo[] tags = snapshot.Tags
            .Where(tag => Matches(tag.Name))
            .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (tags.Length > 0)
        {
            rows.Add(new(NativeBranchPopupRowKind.Group, UiText.TagReferences, "group"));
            rows.AddRange(tags.Select(tag => new NativeBranchPopupRow(
                NativeBranchPopupRowKind.Tag,
                tag.Name,
                "reference",
                Tag: tag)));
        }

        if (rows.All(row => row.Kind == NativeBranchPopupRowKind.Group))
        {
            rows.Add(new(NativeBranchPopupRowKind.Empty, UiText.NoMatchingBranchesOrActions, string.Empty));
        }

        return rows;

        void AddAction(string label, string glyph, NativeBranchPopupCommand command)
        {
            if (Matches(label))
            {
                rows.Add(new(
                    NativeBranchPopupRowKind.Action,
                    label,
                    glyph,
                    new(command)));
            }
        }

        void AddBranchGroup(string label, IEnumerable<GitBranchInfo> source)
        {
            GitBranchInfo[] branches = source.ToArray();
            if (branches.Length == 0)
            {
                return;
            }

            rows.Add(new(NativeBranchPopupRowKind.Group, label, "group"));
            rows.AddRange(branches.Select(branch => new NativeBranchPopupRow(
                NativeBranchPopupRowKind.Branch,
                branch.Name,
                branch.IsCurrent ? "current" : "reference",
                Branch: branch)));
        }
    }

    private static List<NativeBranchPopupRow> BuildActions(NativeBranchPopupRow row)
    {
        if (!row.IsReference)
        {
            return [];
        }

        List<NativeBranchPopupRow> actions = [];
        if (row.Branch is { } branch)
        {
            if (branch.IsRemote)
            {
                Add(UiText.CreateTrackingBranchEllipsis, "checkout", NativeBranchPopupCommand.CreateTrackingBranch);
            }
            else if (!branch.IsCurrent)
            {
                Add(UiText.CheckoutReference, "checkout", NativeBranchPopupCommand.CheckoutBranch);
            }

            Add($"从 {branch.Name} 新建分支…", "add", NativeBranchPopupCommand.CreateBranchFromReference);
            Add(UiText.CompareWithWorkspace, "compare", NativeBranchPopupCommand.CompareWithWorkspace);
            if (!branch.IsRemote)
            {
                Add(UiText.CreateWorktreeEllipsis, "worktree", NativeBranchPopupCommand.CreateWorktree);
                Add(UiText.PushEllipsis, "push", NativeBranchPopupCommand.PushReference);
                Add(UiText.RenameEllipsis, "rename", NativeBranchPopupCommand.RenameReference);
                if (!branch.IsCurrent)
                {
                    Add(UiText.DeleteEllipsis, "delete", NativeBranchPopupCommand.DeleteReference);
                }
            }
        }
        else if (row.Tag is { } tag)
        {
            Add(UiText.CheckoutReference, "checkout", NativeBranchPopupCommand.CheckoutRevision);
            Add($"从 {tag.Name} 新建分支…", "add", NativeBranchPopupCommand.CreateBranchFromReference);
            Add(UiText.CompareWithWorkspace, "compare", NativeBranchPopupCommand.CompareWithWorkspace);
            Add(UiText.PushEllipsis, "push", NativeBranchPopupCommand.PushReference);
            Add(UiText.DeleteEllipsis, "delete", NativeBranchPopupCommand.DeleteReference);
        }

        return actions;

        void Add(string label, string glyph, NativeBranchPopupCommand command)
        {
            actions.Add(new(
                NativeBranchPopupRowKind.Action,
                label,
                glyph,
                new(command, row.Branch, row.Tag)));
        }
    }

    private void CreateControls()
    {
        _searchEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            SearchIdentifier,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            S(PopupPadding),
            S(PopupPadding),
            S(MainPopupWidth - PopupPadding * 2),
            S(SearchHeight));
        _ = NativeMethods.SendMessage(
            _searchEdit,
            NativeMethods.EditSetCueBanner,
            1,
            UiText.SearchBranchesAndActions);
        _referencesList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            ReferencesIdentifier,
            NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxOwnerDrawFixed
                | NativeMethods.ListBoxHasStrings
                | NativeMethods.ListBoxNoIntegralHeight,
            S(PopupPadding),
            S(ListTop),
            S(MainPopupWidth - PopupPadding * 2),
            S(MainPopupMaxHeight - ListTop - PopupPadding));
        _actionsList = CreateControl(
            NativeMethods.ListBoxClass,
            string.Empty,
            ActionsIdentifier,
            NativeMethods.WindowStyleVerticalScroll
                | NativeMethods.ListBoxNotify
                | NativeMethods.ListBoxOwnerDrawFixed
                | NativeMethods.ListBoxHasStrings
                | NativeMethods.ListBoxNoIntegralHeight,
            S(PopupPadding),
            S(PopupPadding),
            S(ActionPopupWidth - PopupPadding * 2),
            S(ActionPopupMaxHeight - PopupPadding * 2),
            _actionsHandle);
        _ = NativeMethods.SendMessage(
            _referencesList,
            NativeMethods.ListBoxSetItemHeight,
            0,
            S(30));
        _ = NativeMethods.SendMessage(
            _actionsList,
            NativeMethods.ListBoxSetItemHeight,
            0,
            S(30));
        AddControlSubclass(_searchEdit, SearchSubclassIdentifier);
        AddControlSubclass(_referencesList, ReferencesSubclassIdentifier);
        AddControlSubclass(_actionsList, ActionsSubclassIdentifier);
        _ = NativeMethods.ShowWindow(_actionsList, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_actionsHandle, NativeMethods.ShowHide);
        LayoutControls();
    }

    private nint CreateControl(
        string className,
        string text,
        int identifier,
        uint specificStyle,
        int x,
        int y,
        int width,
        int height,
        nint? parent = null)
    {
        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleTabStop
                | specificStyle,
            x,
            y,
            width,
            height,
            parent ?? _handle,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InterfaceControlCreateFailed);
        }

        _ = NativeMethods.SendMessage(
            control,
            NativeMethods.WindowMessageSetFont,
            unchecked((nuint)NativeTheme.UiFont),
            1);
        return control;
    }

    private void AddControlSubclass(nint control, nuint identifier)
    {
        if (!NativeMethods.SetWindowSubclass(control, ControlProcedure, identifier, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InterfaceControlCreateFailed);
        }

        lock (InstancesGate)
        {
            ControlInstances.Add(control, this);
        }
    }

    private void RemoveControlSubclasses()
    {
        foreach ((nint control, nuint identifier) in new[]
        {
            (_searchEdit, SearchSubclassIdentifier),
            (_referencesList, ReferencesSubclassIdentifier),
            (_actionsList, ActionsSubclassIdentifier),
        })
        {
            if (control == 0)
            {
                continue;
            }

            _ = NativeMethods.RemoveWindowSubclass(control, ControlProcedure, identifier);
            lock (InstancesGate)
            {
                ControlInstances.Remove(control);
            }
        }
    }

    private void PopulateRows(string query)
    {
        if (_disposed)
        {
            return;
        }

        _rows.Clear();
        _rows.AddRange(BuildRows(_snapshot, query));
        ResizeMainPopup();
        _ = NativeMethods.SendMessage(_referencesList, NativeMethods.ListBoxResetContent, 0, 0);
        foreach (NativeBranchPopupRow row in _rows)
        {
            _ = NativeMethods.SendMessage(
                _referencesList,
                NativeMethods.ListBoxAddString,
                0,
                row.Label);
        }

        _actions.Clear();
        _ = NativeMethods.SendMessage(_actionsList, NativeMethods.ListBoxResetContent, 0, 0);
        _ = NativeMethods.ShowWindow(_actionsHandle, NativeMethods.ShowHide);
        _ = NativeMethods.ShowWindow(_actionsList, NativeMethods.ShowHide);
        SelectFirstSelectable(_referencesList, _rows);
    }

    private void UpdateActions()
    {
        int index = GetSelection(_referencesList);
        _actions.Clear();
        _ = NativeMethods.SendMessage(_actionsList, NativeMethods.ListBoxResetContent, 0, 0);
        if (index < 0 || index >= _rows.Count || !_rows[index].IsReference)
        {
            _ = NativeMethods.ShowWindow(_actionsHandle, NativeMethods.ShowHide);
            _ = NativeMethods.ShowWindow(_actionsList, NativeMethods.ShowHide);
            return;
        }

        _actions.AddRange(BuildActions(_rows[index]));
        foreach (NativeBranchPopupRow row in _actions)
        {
            _ = NativeMethods.SendMessage(
                _actionsList,
                NativeMethods.ListBoxAddString,
                0,
                row.Label);
        }

        PositionActions(index);
        _ = NativeMethods.ShowWindow(_actionsHandle, NativeMethods.ShowWithoutActivate);
        _ = NativeMethods.ShowWindow(_actionsList, NativeMethods.ShowNormal);
    }

    private void PositionActions(int referenceIndex)
    {
        if (_actionsHandle == 0 || !NativeMethods.GetWindowRectangle(_handle, out NativeMethods.Rectangle main))
        {
            return;
        }

        int actionHeight = S(CalculatePopupHeight(_actions.Count, ActionPopupMinHeight, ActionPopupMaxHeight, PopupPadding));
        int x = main.Right + S(6);
        int y = main.Top + S(ListTop + referenceIndex * RowHeight);
        nint monitor = NativeMethods.MonitorFromWindow(_owner, NativeMethods.MonitorDefaultToNearest);
        NativeMethods.MonitorInfo info = new() { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor != 0 && NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            if (x + S(ActionPopupWidth) > info.WorkArea.Right)
            {
                x = main.Left - S(ActionPopupWidth) - S(6);
            }

            x = Math.Clamp(x, info.WorkArea.Left, Math.Max(info.WorkArea.Left, info.WorkArea.Right - S(ActionPopupWidth)));
            y = Math.Clamp(y, info.WorkArea.Top, Math.Max(info.WorkArea.Top, info.WorkArea.Bottom - actionHeight));
        }

        _ = NativeMethods.MoveWindow(_actionsHandle, x, y, S(ActionPopupWidth), actionHeight, true);
        SetPopupRegion(_actionsHandle, S(ActionPopupWidth), actionHeight);
        LayoutControls();
    }

    private void ResizeMainPopup()
    {
        if (_handle == 0 || !NativeMethods.GetWindowRectangle(_handle, out NativeMethods.Rectangle rectangle))
        {
            return;
        }

        int height = S(CalculatePopupHeight(_rows.Count, MainPopupMinHeight, MainPopupMaxHeight));
        _ = NativeMethods.MoveWindow(_handle, rectangle.Left, rectangle.Top, S(MainPopupWidth), height, true);
        SetPopupRegion(_handle, S(MainPopupWidth), height);
        LayoutControls();
    }

    private void LayoutControls()
    {
        if (_handle != 0 && NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle main))
        {
            int width = Math.Max(0, main.Right - main.Left);
            int height = Math.Max(0, main.Bottom - main.Top);
            Move(_searchEdit, PopupPadding, PopupPadding, MainPopupWidth - PopupPadding * 2, SearchHeight);
            Move(
                _referencesList,
                PopupPadding,
                ListTop,
                MainPopupWidth - PopupPadding * 2,
                Math.Max(RowHeight, (int)Math.Max(0, NativeTheme.Unscale(height) - ListTop - PopupPadding)));
        }

        if (_actionsHandle != 0 && NativeMethods.GetClientRectangle(_actionsHandle, out NativeMethods.Rectangle actions))
        {
            int width = Math.Max(0, actions.Right - actions.Left);
            int height = Math.Max(0, actions.Bottom - actions.Top);
            Move(
                _actionsList,
                PopupPadding,
                PopupPadding,
                Math.Max(RowHeight, (int)Math.Max(0, NativeTheme.Unscale(width) - PopupPadding * 2)),
                Math.Max(RowHeight, (int)Math.Max(0, NativeTheme.Unscale(height) - PopupPadding * 2)));
        }
    }

    private static void Move(nint control, int x, int y, int width, int height)
    {
        if (control != 0)
        {
            _ = NativeMethods.MoveWindow(control, S(x), S(y), S(width), S(height), true);
        }
    }

    private static int CalculatePopupHeight(
        int rowCount,
        int minimum,
        int maximum,
        int contentTop = ListTop)
    {
        int safeRows = Math.Max(1, rowCount);
        return Math.Clamp(contentTop + safeRows * RowHeight + PopupPadding, minimum, maximum);
    }

    private static void SetPopupRegion(nint window, int width, int height)
    {
        if (window == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        nint region = NativeMethods.CreateRoundRectangleRegion(0, 0, width + 1, height + 1, S(8), S(8));
        if (region != 0)
        {
            _ = NativeMethods.SetWindowRegion(window, region, true);
        }
    }

    private void ActivateCurrent(nint control)
    {
        List<NativeBranchPopupRow> source = control == _actionsList ? _actions : _rows;
        int index = GetSelection(control);
        if (index < 0 || index >= source.Count)
        {
            return;
        }

        NativeBranchPopupRow row = source[index];
        if (row.IsReference)
        {
            UpdateActions();
            if (_actions.Count > 0)
            {
                _ = NativeMethods.SetFocus(_actionsList);
            }
            return;
        }

        if (row.Request is not { } request)
        {
            return;
        }

        Dispose();
        _ = _execute(request);
    }

    private void HandleCommand(nuint wordParameter)
    {
        int command = NativeMethods.LowWord(wordParameter);
        int notification = NativeMethods.HighWord(wordParameter);
        if (command == SearchIdentifier && notification == EditNotificationChanged)
        {
            PopulateRows(NativeMethods.GetWindowTextValue(_searchEdit));
            return;
        }

        if (command == ReferencesIdentifier
            && notification == NativeMethods.ListBoxNotificationSelectionChanged)
        {
            UpdateActions();
        }
    }

    private bool DrawItem(nint longParameter)
    {
        if (longParameter == 0)
        {
            return false;
        }

        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(longParameter);
        List<NativeBranchPopupRow> source = item.ControlIdentifier == ActionsIdentifier ? _actions : _rows;
        int index = unchecked((int)item.ItemIdentifier);
        if (index < 0 || index >= source.Count)
        {
            return true;
        }

        NativeBranchPopupRow row = source[index];
        NativeThemePalette palette = NativeTheme.Palette(NativeTheme.IsDark(_settings.Theme));
        bool selected = (item.ItemState & NativeMethods.OwnerDrawSelected) != 0 && row.IsSelectable;
        Fill(item.DeviceContext, item.ItemRectangle, selected ? palette.AccentSoft : palette.Panel);

        if (IsSeparatorIndex(unchecked((int)item.ControlIdentifier), source, index))
        {
            NativeMethods.Rectangle separator = item.ItemRectangle;
            separator.Left += S(8);
            separator.Right -= S(8);
            separator.Bottom = separator.Top + S(1);
            Fill(item.DeviceContext, separator, palette.Border);
        }

        NativeMethods.Rectangle glyphRectangle = item.ItemRectangle;
        glyphRectangle.Left += S(8);
        glyphRectangle.Right = glyphRectangle.Left + S(22);
        NativeMethods.Rectangle textRectangle = item.ItemRectangle;
        textRectangle.Left += S(35);
        textRectangle.Right -= S(row.IsReference ? 24 : 8);
        uint color = row.Kind == NativeBranchPopupRowKind.Group ? palette.Text : palette.Text;
        nint font = row.Kind == NativeBranchPopupRowKind.Group ? NativeTheme.UiMediumFont : NativeTheme.UiFont;
        DrawBranchPopupGlyph(
            item.DeviceContext,
            glyphRectangle,
            row,
            row.Branch?.IsCurrent == true ? palette.Accent : palette.Muted);
        DrawText(item.DeviceContext, row.Label, textRectangle, color, font);
        if (row.IsReference)
        {
            NativeMethods.Rectangle arrow = item.ItemRectangle;
            arrow.Left = arrow.Right - S(24);
            arrow.Right -= S(8);
            _ = NativeTheme.DrawNavigationIcon(
                item.DeviceContext,
                arrow,
                NativeNavigationIcon.Right,
                palette.Muted);
        }

        return true;
    }

    private static void DrawBranchPopupGlyph(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        NativeBranchPopupRow row,
        uint color)
    {
        int centerX = (rectangle.Left + rectangle.Right) / 2;
        int centerY = (rectangle.Top + rectangle.Bottom) / 2;
        float unit = NativeTheme.Scale(1f);
        List<NativeGdiPlusDrawing.StrokeLine> lines = [];
        List<NativeGdiPlusDrawing.StrokeEllipse> ellipses = [];
        if (row.Kind == NativeBranchPopupRowKind.Group)
        {
            lines.AddRange([
                new(centerX - 4 * unit, centerY - 1 * unit, centerX, centerY + 3 * unit),
                new(centerX, centerY + 3 * unit, centerX + 4 * unit, centerY - 1 * unit),
            ]);
        }
        else if (row.IsReference)
        {
            lines.AddRange([
                new(centerX, centerY - 5 * unit, centerX + 4 * unit, centerY),
                new(centerX + 4 * unit, centerY, centerX, centerY + 5 * unit),
                new(centerX, centerY + 5 * unit, centerX - 4 * unit, centerY),
                new(centerX - 4 * unit, centerY, centerX, centerY - 5 * unit),
            ]);
            if (row.Branch?.IsCurrent == true)
            {
                ellipses.Add(new(centerX - 1.5f * unit, centerY - 1.5f * unit, 3 * unit, 3 * unit));
            }
        }
        else
        {
            switch (row.Glyph)
            {
                case "update":
                    lines.AddRange([
                        new(centerX - 5 * unit, centerY - 4 * unit, centerX + 3 * unit, centerY + 4 * unit),
                        new(centerX + 3 * unit, centerY + 4 * unit, centerX - 1 * unit, centerY + 4 * unit),
                        new(centerX + 3 * unit, centerY + 4 * unit, centerX + 3 * unit, centerY),
                    ]);
                    break;
                case "commit":
                    _ = NativeTheme.DrawToolWindowIcon(deviceContext, rectangle, NativeToolWindowIcon.Commit, color);
                    return;
                case "push":
                    lines.AddRange([
                        new(centerX - 4 * unit, centerY + 4 * unit, centerX + 4 * unit, centerY - 4 * unit),
                        new(centerX + 4 * unit, centerY - 4 * unit, centerX, centerY - 4 * unit),
                        new(centerX + 4 * unit, centerY - 4 * unit, centerX + 4 * unit, centerY),
                    ]);
                    break;
                case "add":
                    lines.AddRange([
                        new(centerX - 5 * unit, centerY, centerX + 5 * unit, centerY),
                        new(centerX, centerY - 5 * unit, centerX, centerY + 5 * unit),
                    ]);
                    break;
                case "checkout":
                case "compare":
                    lines.AddRange([
                        new(centerX - 6 * unit, centerY - 3 * unit, centerX + 5 * unit, centerY - 3 * unit),
                        new(centerX + 5 * unit, centerY - 3 * unit, centerX + 2 * unit, centerY - 6 * unit),
                        new(centerX + 5 * unit, centerY - 3 * unit, centerX + 2 * unit, centerY),
                        new(centerX + 6 * unit, centerY + 3 * unit, centerX - 5 * unit, centerY + 3 * unit),
                        new(centerX - 5 * unit, centerY + 3 * unit, centerX - 2 * unit, centerY),
                        new(centerX - 5 * unit, centerY + 3 * unit, centerX - 2 * unit, centerY + 6 * unit),
                    ]);
                    break;
                case "worktree":
                    lines.AddRange([
                        new(centerX - 5 * unit, centerY - 5 * unit, centerX + 5 * unit, centerY - 5 * unit),
                        new(centerX + 5 * unit, centerY - 5 * unit, centerX + 5 * unit, centerY + 5 * unit),
                        new(centerX + 5 * unit, centerY + 5 * unit, centerX - 5 * unit, centerY + 5 * unit),
                        new(centerX - 5 * unit, centerY + 5 * unit, centerX - 5 * unit, centerY - 5 * unit),
                    ]);
                    break;
                case "rename":
                    _ = NativeTheme.DrawRenameIcon(deviceContext, centerX, centerY, color);
                    return;
                case "delete":
                    lines.AddRange([
                        new(centerX - 4 * unit, centerY - 4 * unit, centerX + 4 * unit, centerY + 4 * unit),
                        new(centerX + 4 * unit, centerY - 4 * unit, centerX - 4 * unit, centerY + 4 * unit),
                    ]);
                    break;
            }
        }

        if (lines.Count > 0 || ellipses.Count > 0)
        {
            _ = NativeGdiPlusDrawing.StrokeShapes(
                deviceContext,
                color,
                Math.Max(1f, NativeTheme.Scale(1.5f)),
                CollectionsMarshal.AsSpan(lines),
                CollectionsMarshal.AsSpan(ellipses),
                []);
        }
    }

    private void Paint(nint window)
    {
        nint deviceContext = NativeMethods.BeginPaint(window, out NativeMethods.PaintStructure paint);
        if (deviceContext == 0)
        {
            return;
        }

        try
        {
            NativeThemePalette palette = NativeTheme.Palette(NativeTheme.IsDark(_settings.Theme));
            if (NativeMethods.GetClientRectangle(window, out NativeMethods.Rectangle client))
            {
                Fill(deviceContext, client, palette.Panel);
                NativeMethods.Rectangle top = client;
                top.Bottom = Math.Min(client.Bottom, S(1));
                Fill(deviceContext, top, palette.BorderStrong);
                NativeMethods.Rectangle left = client;
                left.Right = Math.Min(client.Right, S(1));
                Fill(deviceContext, left, palette.BorderStrong);
                NativeMethods.Rectangle right = client;
                right.Left = Math.Max(client.Left, client.Right - S(1));
                Fill(deviceContext, right, palette.BorderStrong);
                NativeMethods.Rectangle bottom = client;
                bottom.Top = Math.Max(client.Top, client.Bottom - S(1));
                Fill(deviceContext, bottom, palette.BorderStrong);
            }
        }
        finally
        {
            _ = NativeMethods.EndPaint(_handle, ref paint);
        }
    }

    private void ApplyAppearance()
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, dark);
        NativeTheme.ApplyToWindow(_actionsHandle, dark);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        _controlBrush = NativeMethods.CreateSolidBrush(palette.Panel);
        foreach (nint control in new[] { _searchEdit, _referencesList, _actionsList })
        {
            NativeTheme.ApplyToControl(control, dark);
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
                throw new Win32Exception(error, UiText.MainWindowClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeBranchPopup? instance;
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
            case NativeMethods.WindowMessageActivate:
                if (NativeMethods.LowWord(wordParameter) == NativeMethods.WindowActivationInactive)
                {
                    nint foreground = NativeMethods.GetForegroundWindow();
                    bool belongsToPopup = foreground == instance._handle
                        || foreground == instance._actionsHandle
                        || NativeMethods.GetAncestor(foreground, NativeMethods.GetAncestorRoot) is var root
                            && (root == instance._handle || root == instance._actionsHandle);
                    nint focus = NativeMethods.GetFocus();
                    bool focusBelongsToPopup = focus == instance._handle
                        || focus == instance._actionsHandle
                        || NativeMethods.IsChild(instance._handle, focus)
                        || NativeMethods.IsChild(instance._actionsHandle, focus);
                    if (!belongsToPopup && !focusBelongsToPopup)
                    {
                        instance.Dispose();
                        return 0;
                    }
                }
                break;
            case NativeMethods.WindowMessageCommand:
                instance.HandleCommand(wordParameter);
                return 0;
            case NativeMethods.WindowMessageDrawItem:
                if (instance.DrawItem(longParameter))
                {
                    return 1;
                }
                break;
            case NativeMethods.WindowMessagePaint:
                instance.Paint(window);
                return 0;
            case NativeMethods.WindowMessageEraseBackground:
                return 1;
            case NativeMethods.WindowMessageClose:
                instance.Dispose();
                return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private static nint HandleControlMessage(
        nint window,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        _ = referenceData;
        NativeBranchPopup? instance;
        lock (InstancesGate)
        {
            ControlInstances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        }

        if (message == NativeMethods.WindowMessageKeyDown)
        {
            int key = unchecked((int)wordParameter);
            if (key == NativeMethods.VirtualKeyEscape)
            {
                instance.Dispose();
                return 0;
            }

            if (subclassIdentifier == SearchSubclassIdentifier && key == NativeMethods.VirtualKeyDown)
            {
                SelectFirstSelectable(instance._referencesList, instance._rows);
                _ = NativeMethods.SetFocus(instance._referencesList);
                return 0;
            }

            if (subclassIdentifier == ReferencesSubclassIdentifier && key == NativeMethods.VirtualKeyRight)
            {
                instance.UpdateActions();
                if (instance._actions.Count > 0)
                {
                    _ = NativeMethods.SetForegroundWindow(instance._actionsHandle);
                    _ = NativeMethods.SetFocus(instance._actionsList);
                }
                return 0;
            }

            if (subclassIdentifier == ActionsSubclassIdentifier && key == NativeMethods.VirtualKeyLeft)
            {
                _ = NativeMethods.SetForegroundWindow(instance._handle);
                _ = NativeMethods.SetFocus(instance._referencesList);
                return 0;
            }

            if (key == NativeMethods.VirtualKeyEnter)
            {
                instance.ActivateCurrent(window);
                return 0;
            }
        }

        if (message == NativeMethods.WindowMessageLeftButtonUp
            && subclassIdentifier is ReferencesSubclassIdentifier or ActionsSubclassIdentifier)
        {
            nint result = NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
            instance.ActivateCurrent(window);
            return result;
        }

        return NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
    }

    private static void SelectFirstSelectable(nint list, List<NativeBranchPopupRow> rows)
    {
        int index = Enumerable.Range(0, rows.Count).FirstOrDefault(index => rows[index].IsSelectable, -1);
        _ = NativeMethods.SendMessage(
            list,
            NativeMethods.ListBoxSetCurrentSelection,
            index < 0 ? unchecked((nuint)(-1)) : unchecked((nuint)index),
            0);
    }

    private static int GetSelection(nint list)
    {
        return checked((int)NativeMethods.SendMessage(list, NativeMethods.ListBoxGetCurrentSelection, 0, 0));
    }

    private static (int X, int Y) ConstrainToWorkArea(
        nint owner,
        int anchorX,
        int anchorY,
        int width,
        int height)
    {
        nint monitor = NativeMethods.MonitorFromWindow(owner, NativeMethods.MonitorDefaultToNearest);
        NativeMethods.MonitorInfo info = new() { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return (anchorX, anchorY);
        }

        int x = Math.Clamp(anchorX, info.WorkArea.Left, Math.Max(info.WorkArea.Left, info.WorkArea.Right - width));
        int y = anchorY + height <= info.WorkArea.Bottom
            ? anchorY
            : Math.Max(info.WorkArea.Top, info.WorkArea.Bottom - height);
        return (x, y);
    }

    private static void Fill(nint deviceContext, NativeMethods.Rectangle rectangle, uint color)
    {
        nint brush = NativeMethods.CreateSolidBrush(color);
        if (brush == 0)
        {
            return;
        }

        _ = NativeMethods.FillRectangle(deviceContext, ref rectangle, brush);
        _ = NativeMethods.DeleteObject(brush);
    }

    private static void DrawText(
        nint deviceContext,
        string text,
        NativeMethods.Rectangle rectangle,
        uint color,
        nint font)
    {
        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(deviceContext, color);
        uint format = NativeMethods.DrawTextVerticalCenter
            | NativeMethods.DrawTextSingleLine
            | NativeMethods.DrawTextNoPrefix
            | NativeMethods.DrawTextEndEllipsis;
        _ = NativeMethods.DrawText(deviceContext, text, text.Length, ref rectangle, format);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
    }

    private static bool IsSeparatorIndex(
        int controlIdentifier,
        List<NativeBranchPopupRow> rows,
        int index)
    {
        if (controlIdentifier == ActionsIdentifier)
        {
            return rows.Count >= 4 && index == 3;
        }

        // 快捷动作与引用分组之间使用与视觉稿一致的细分隔线。
        int firstGroup = rows.FindIndex(row => row.Kind == NativeBranchPopupRowKind.Group);
        if (firstGroup <= 0)
        {
            return false;
        }

        int actionCount = rows.Take(firstGroup).Count(row => row.Kind == NativeBranchPopupRowKind.Action);
        return actionCount >= 3 && index == 3
            || actionCount >= 5 && index == 5;
    }

    private static int S(int logicalPixels) => NativeTheme.Scale(logicalPixels);
}
