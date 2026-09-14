using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class NativeGitHistoryPanel
{
    private const nuint DetailsSubclassIdentifier = 4;
    private static readonly Dictionary<nint, NativeGitHistoryPanel> DetailsInstances = [];
    private static readonly NativeMethods.SubclassProcedure DetailsProcedure = HandleDetailsMessage;
    private const uint DetailsLayoutCompletedMessage = NativeMethods.WindowMessageApp + 91;
    private readonly object _detailsLayoutGate = new();
    private NativeGdiPlusDrawing.TextBlock[] _detailsBlocks = [];
    private CancellationTokenSource? _detailsLayoutCancellation;
    private Task _detailsLayoutTask = Task.CompletedTask;
    private (int Version, NativeCommitDetailsLayout? Layout, string? Error, bool Complete)? _detailsCompletedLayout;
    private int _detailsLayoutVersion;
    private bool _detailsLayoutClosed;
    private bool _detailsLayoutPending;
    private bool _detailsScrollToEnd;
    private string? _detailsLayoutError;
    private int _detailsScrollPosition;
    private int _detailsContentHeight;
    private int _detailsOuterWidth;
    private int _detailsMeasuredWidth;
    private int _detailsWheelRemainder;
    private bool _detailsLayoutDirty = true;
    private bool _updatingDetailsScroll;
    private int _detailsLayoutCount;
    internal int DetailsLastDrawnCharactersForTest { get; private set; }

    internal int DetailsScrollPositionForTest => _detailsScrollPosition;
    internal int DetailsLayoutCountForTest => _detailsLayoutCount;
    internal bool DetailsLayoutPendingForTest => _detailsLayoutPending;
    internal Task DetailsLayoutTaskForTest => _detailsLayoutTask;
    internal string? DetailsLayoutErrorForTest => _detailsLayoutError;
    internal string DetailsBodyForTest => _detailsBody ?? string.Empty;
    internal static int DetailsRegistrationCountForTest
    {
        get { lock (InstancesGate) { return DetailsInstances.Count; } }
    }

    private bool ShowFilesContextMenu(nint parameter)
    {
        if (_operationRunning) return true;
        int index = GetListSelection(_filesList);
        int x, y;
        if (parameter == -1)
        {
            if (!TryGetListItemScreenPoint(_filesList, index, out x, out y)) return true;
        }
        else
        {
            x = unchecked((short)((long)parameter & 0xffff));
            y = unchecked((short)(((long)parameter >> 16) & 0xffff));
            NativeMethods.Point point = new() { X = x, Y = y };
            if (!NativeMethods.ScreenToClient(_filesList, ref point)) return true;
            nint hit = NativeMethods.SendMessage(_filesList, 0x01a9, 0,
                unchecked((nint)((point.Y << 16) | (point.X & 0xffff))));
            if (((long)hit >> 16) != 0) return true;
            index = (int)((long)hit & 0xffff);
        }
        if (index < 0 || index >= _fileRows.Count || _fileRows[index].File is null) return true;
        if (index != GetListSelection(_filesList)) CancelPendingFileDiff();
        _ = NativeMethods.SendMessage(_filesList, NativeMethods.ListBoxSetCurrentSelection, (nuint)index, 0);
        _ = NativeMethods.SetFocus(_filesList);
        _contextMenu?.Dispose();
        _contextMenu = NativeContextMenu.Show(Handle, x, y,
        [
            new(UiText.ShowDiff, NativeContextMenuIcon.Compare, ActivateSelectedFileDiff),
            new(UiText.FileHistory, NativeContextMenuIcon.History, ShowFileHistory),
            new(UiText.Blame, NativeContextMenuIcon.Blame, () => _ = ShowBlameAsync()),
        ], NativeTheme.IsDark(_settings.Theme));
        return true;
    }

    private void AttachDetailsScrolling()
    {
        lock (InstancesGate) { DetailsInstances.Add(_metadataLabel, this); }
        if (!NativeMethods.SetWindowSubclass(_metadataLabel, DetailsProcedure, DetailsSubclassIdentifier, 0))
        {
            lock (InstancesGate) { DetailsInstances.Remove(_metadataLabel); }
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建提交详情滚动区域。");
        }
    }

    private void DetachDetailsScrolling()
    {
        lock (_detailsLayoutGate)
        {
            _detailsLayoutClosed = true;
            _detailsCompletedLayout = null;
        }
        _detailsLayoutCancellation?.Cancel();
        _detailsLayoutCancellation?.Dispose();
        _detailsLayoutCancellation = null;
        if (_metadataLabel == 0) return;
        _ = NativeMethods.RemoveWindowSubclass(_metadataLabel, DetailsProcedure, DetailsSubclassIdentifier);
        lock (InstancesGate) { DetailsInstances.Remove(_metadataLabel); }
        _detailsBlocks = [];
        _detailsBody = null;
    }

    private void ResetDetailsLayout()
    {
        _detailsLayoutCancellation?.Cancel();
        Interlocked.Increment(ref _detailsLayoutVersion);
        lock (_detailsLayoutGate) { _detailsCompletedLayout = null; }
        _detailsLayoutPending = false;
        _detailsScrollPosition = 0;
        _detailsWheelRemainder = 0;
        _detailsScrollToEnd = false;
        _detailsBlocks = [];
        _detailsContentHeight = 0;
        _detailsLayoutDirty = true;
        UpdateDetailsScrollRange();
        _ = NativeMethods.InvalidateRectangle(_metadataLabel, 0, false);
    }

    private void UpdateDetailsScrollRange()
    {
        if (_updatingDetailsScroll || _disposed || _metadataLabel == 0
            || !NativeMethods.IsWindowVisible(_metadataLabel)
            || !NativeMethods.GetClientRectangle(_metadataLabel, out var client)
            || !NativeMethods.GetWindowRectangle(_metadataLabel, out var outer)
            || client.Bottom <= 0 || outer.Right <= outer.Left) return;
        _updatingDetailsScroll = true;
        try
        {
            int width = outer.Right - outer.Left;
            if (_detailsLayoutDirty || _detailsOuterWidth != width)
            {
                _detailsOuterWidth = width;
                _detailsLayoutDirty = false;
                QueueDetailsLayout(client.Right);
            }
            int maximum = Math.Max(0, _detailsContentHeight - client.Bottom);
            if (!_detailsLayoutPending) _detailsScrollPosition = _detailsScrollToEnd ? maximum : Math.Min(_detailsScrollPosition, maximum);
            DetailsScrollInfo info = new()
            {
                Size = (uint)Marshal.SizeOf<DetailsScrollInfo>(),
                Mask = 7,
                Maximum = Math.Max(0, _detailsContentHeight - 1),
                Page = (uint)client.Bottom,
                Position = _detailsScrollPosition,
            };
            _ = SetDetailsScrollInfo(_metadataLabel, 1, ref info, true);
            // 原生滚动条宽度取真实客户区，不把审计 DPI 的模拟比例当作系统非客户区度量。
            if (NativeMethods.GetClientRectangle(_metadataLabel, out var actual) && actual.Right != _detailsMeasuredWidth)
            {
                QueueDetailsLayout(actual.Right);
            }
        }
        finally { _updatingDetailsScroll = false; }
    }

    private void QueueDetailsLayout(int width)
    {
        _detailsLayoutCount++;
        _detailsMeasuredWidth = width;
        _detailsLayoutCancellation?.Cancel();
        _detailsLayoutCancellation?.Dispose();
        _detailsLayoutCancellation = null;
        int version = Interlocked.Increment(ref _detailsLayoutVersion);
        _detailsLayoutError = null;
        lock (_detailsLayoutGate) { _detailsCompletedLayout = null; }
        if (_detailsTitle is null || string.IsNullOrEmpty(_detailsBody))
        {
            _detailsLayoutPending = false;
            _detailsBlocks = [];
            _detailsContentHeight = _detailsTitle is null ? 0 : CalculateCommitDetailsTextLayout(NativeTheme.Scale(8), 0,
                !string.IsNullOrWhiteSpace(_detailsReferences)).BodyTop + NativeTheme.Scale(8);
            return;
        }
        int top = CalculateCommitDetailsTextLayout(NativeTheme.Scale(8), 0,
            !string.IsNullOrWhiteSpace(_detailsReferences)).BodyTop;
        string body = _detailsBody;
        string family = NativeFontResolver.ResolveMonospace(_settings.MonospaceFontFamily);
        int height = (int)Math.Round(NativeTheme.Scale((float)Math.Clamp(_settings.FontSize, 9, 40)));
        int contentWidth = Math.Max(1, width - NativeTheme.Scale(20));
        int padding = NativeTheme.Scale(8);
        bool showFirstBlock = _detailsBlocks.Length == 0;
        _detailsLayoutCancellation = new();
        CancellationToken token = _detailsLayoutCancellation.Token;
        _detailsLayoutPending = true;
        Task previous = _detailsLayoutTask;
        _detailsLayoutTask = Task.Run(async () =>
        {
            // 旧任务在有界块之间取消；新任务接续它，快速拖动时最多一个排版任务实际占用资源。
            await previous.ConfigureAwait(false);
            NativeCommitDetailsLayout? result = null;
            string? error = null;
            void Publish(NativeCommitDetailsLayout? layout, string? failure, bool complete)
            {
                lock (_detailsLayoutGate)
                {
                    if (_detailsLayoutClosed || token.IsCancellationRequested || version != _detailsLayoutVersion) return;
                    _detailsCompletedLayout = (version, layout, failure, complete);
                    _ = NativeMethods.PostMessage(_metadataLabel, DetailsLayoutCompletedMessage, 0, 0);
                }
            }
            try
            {
                result = NativeCommitDetailsLayout.Create(body, family, height, contentWidth, top, padding,
                    token, first => { if (showFirstBlock) Publish(first, null, complete: false); });
            }
            catch (OperationCanceledException) { return; }
            catch (Exception exception) when (exception is Win32Exception or ExternalException or OverflowException)
            { error = exception.Message; }
            Publish(result, error, complete: true);
        });
    }

    private void AcceptDetailsLayout()
    {
        (int Version, NativeCommitDetailsLayout? Layout, string? Error, bool Complete)? completed;
        lock (_detailsLayoutGate)
        {
            completed = _detailsCompletedLayout;
            _detailsCompletedLayout = null;
        }
        if (completed is not { } value || value.Version != _detailsLayoutVersion || _disposed) return;
        _detailsLayoutPending = !value.Complete;
        _detailsLayoutError = value.Error;
        if (value.Layout is { } layout)
        {
            _detailsBlocks = layout.Blocks;
            _detailsContentHeight = layout.Height;
        }
        UpdateDetailsScrollRange();
        _ = NativeMethods.InvalidateRectangle(_metadataLabel, 0, false);
    }

    private void DrawDetailsBody(nint dc, NativeMethods.Rectangle viewport, uint color)
    {
        DetailsLastDrawnCharactersForTest = 0;
        if (string.IsNullOrEmpty(_detailsBody)) return;
        if (_detailsLayoutError is { } error)
        {
            viewport.Top = CalculateCommitDetailsTextLayout(NativeTheme.Scale(8), 0,
                !string.IsNullOrWhiteSpace(_detailsReferences)).BodyTop;
            viewport.Left += NativeTheme.Scale(10);
            _ = NativeMethods.DrawText(dc, error, error.Length, ref viewport,
                NativeMethods.DrawTextWordBreak | NativeMethods.DrawTextNoPrefix);
            return;
        }
        int low = 0, high = _detailsBlocks.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            NativeGdiPlusDrawing.TextBlock candidate = _detailsBlocks[middle];
            if (candidate.Top + candidate.Height <= _detailsScrollPosition) low = middle + 1;
            else high = middle;
        }
        nint previous = NativeMethods.SelectObject(dc, _detailsBodyFont != 0 ? _detailsBodyFont : NativeTheme.UiFont);
        GCHandle pinned = GCHandle.Alloc(_detailsBody, GCHandleType.Pinned);
        try
        {
            using NativeGdiPlusDrawing.WrappedTextSession text = new(dc);
            for (int index = low; index < _detailsBlocks.Length; index++)
            {
                NativeGdiPlusDrawing.TextBlock block = _detailsBlocks[index];
                float blockTop = (float)(block.Top - _detailsScrollPosition);
                if (blockTop >= viewport.Bottom) break;
                DetailsLastDrawnCharactersForTest += block.Length;
                text.Draw(pinned.AddrOfPinnedObject(), block, viewport.Left + NativeTheme.Scale(10), blockTop, color);
            }
        }
        catch (Win32Exception exception) { _detailsLayoutError = exception.Message; }
        finally
        {
            pinned.Free();
            _ = NativeMethods.SelectObject(dc, previous);
        }
    }

    private void ScrollDetails(long position)
    {
        if (!NativeMethods.GetClientRectangle(_metadataLabel, out var client)) return;
        int next = (int)Math.Clamp(position, 0, Math.Max(0, _detailsContentHeight - client.Bottom));
        if (next == _detailsScrollPosition) return;
        _detailsScrollPosition = next;
        DetailsScrollInfo info = new()
        {
            Size = (uint)Marshal.SizeOf<DetailsScrollInfo>(),
            Mask = 4,
            Position = next,
        };
        _ = SetDetailsScrollInfo(_metadataLabel, 1, ref info, true);
        _ = NativeMethods.InvalidateRectangle(_metadataLabel, 0, false);
    }

    private static nint HandleDetailsMessage(nint window, uint message, nuint wordParameter, nint longParameter,
        nuint subclassIdentifier, nuint referenceData)
    {
        NativeGitHistoryPanel? instance;
        lock (InstancesGate) { DetailsInstances.TryGetValue(window, out instance); }
        if (instance is null) return NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        if (message == DetailsLayoutCompletedMessage) { instance.AcceptDetailsLayout(); return 0; }
        if (message == 0x0082)
        {
            instance.DetachDetailsScrolling();
            return NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        }
        if (message == NativeMethods.WindowMessageSize) instance.UpdateDetailsScrollRange();
        if (message == 0x0018)
        {
            if (wordParameter == 0)
            {
                instance._detailsWheelRemainder = 0;
                if (instance._detailsLayoutPending)
                {
                    instance._detailsLayoutCancellation?.Cancel();
                    Interlocked.Increment(ref instance._detailsLayoutVersion);
                    instance._detailsLayoutPending = false;
                    instance._detailsLayoutDirty = true;
                }
            }
            else instance.UpdateDetailsScrollRange();
        }
        if (NativeMethods.IsWindowVisible(window) && NativeMethods.IsWindowEnabled(window))
        {
            if (message == NativeMethods.WindowMessageLeftButtonDown) _ = NativeMethods.SetFocus(window);
            if (message == NativeMethods.WindowMessageMouseWheel)
            {
                int delta = unchecked((short)(wordParameter >> 16));
                instance._detailsWheelRemainder += delta * NativeTheme.UiLineHeight * 3;
                int pixels = instance._detailsWheelRemainder / 120;
                instance._detailsWheelRemainder %= 120;
                instance._detailsScrollToEnd = false;
                instance.ScrollDetails((long)instance._detailsScrollPosition - pixels);
                return 0;
            }
            if (message == NativeMethods.WindowMessageVerticalScroll)
            {
                int command = (int)(wordParameter & 0xffff);
                DetailsScrollInfo info = new() { Size = (uint)Marshal.SizeOf<DetailsScrollInfo>(), Mask = 0x10 };
                _ = GetDetailsScrollInfo(window, 1, ref info);
                instance.HandleDetailsScrollCommand(command, info.TrackPosition);
                return 0;
            }
            if (message == NativeMethods.WindowMessageKeyDown)
            {
                int command = (int)wordParameter switch
                {
                    NativeMethods.VirtualKeyUp => 0,
                    NativeMethods.VirtualKeyDown => 1,
                    0x21 => 2,
                    0x22 => 3,
                    0x24 => 6,
                    0x23 => 7,
                    _ => -1,
                };
                if (command >= 0)
                {
                    instance.HandleDetailsScrollCommand(command, 0);
                    return 0;
                }
            }
        }
        return NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
    }

    private void HandleDetailsScrollCommand(int command, int trackPosition)
    {
        // SB_ENDSCROLL 等非导航通知不能清除待完成的 End 请求或高精度滚轮余量。
        if (command is < 0 or > 7) return;
        _detailsScrollToEnd = command == 7;
        _detailsWheelRemainder = 0;
        _ = NativeMethods.GetClientRectangle(_metadataLabel, out var client);
        long position = _detailsScrollPosition;
        int line = NativeTheme.UiLineHeight;
        int page = Math.Max(line, client.Bottom - line);
        ScrollDetails(command switch
        {
            0 => position - line,
            1 => position + line,
            2 => position - page,
            3 => position + page,
            4 or 5 => trackPosition,
            6 => 0,
            7 => _detailsContentHeight,
            _ => position,
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DetailsScrollInfo
    {
        internal uint Size, Mask;
        internal int Minimum, Maximum;
        internal uint Page;
        internal int Position, TrackPosition;
    }

    [DllImport("user32.dll", EntryPoint = "SetScrollInfo")]
    private static extern int SetDetailsScrollInfo(nint window, int bar, ref DetailsScrollInfo info,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("user32.dll", EntryPoint = "GetScrollInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDetailsScrollInfo(nint window, int bar, ref DetailsScrollInfo info);

}
