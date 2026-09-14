using System.Runtime.InteropServices;
using Augit.Core.Documents;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeTextPromptTests
{
    [TestMethod]
    public async Task 输入框按Enter返回输入并恢复原工作区焦点()
    {
        using PromptSession session = new();
        await session.ReadyAsync();
        Assert.AreEqual(session.Edit, session.Focus);
        _ = NativeMethods.SetWindowText(session.Edit, "feature/中文引用");
        session.Key(NativeMethods.VirtualKeyEnter);
        Assert.AreEqual("feature/中文引用", await session.ResultAsync());
        session.AssertRestored();
    }

    [TestMethod]
    public async Task Esc取消不返回输入且关闭整个模态遮罩()
    {
        using PromptSession session = new();
        await session.ReadyAsync();
        _ = NativeMethods.SetWindowText(session.Edit, "123");
        session.Key(NativeMethods.VirtualKeyEscape);
        Assert.IsNull(await session.ResultAsync());
        session.AssertRestored();
    }

    [TestMethod]
    public async Task 从文档打开时整个主窗口处于模态禁用状态()
    {
        using PromptSession session = new();
        await session.ReadyAsync();
        Assert.IsFalse(NativeMethods.IsWindowEnabled(session.Owner));
        Assert.AreNotEqual((nint)0, FindWindowEx(session.Owner, 0, "Augit.ModalScrim.Native", null));
    }

    [TestMethod]
    public async Task Tab沿输入取消确定关闭循环且取消按钮Enter不提交()
    {
        using PromptSession session = new();
        await session.ReadyAsync();
        foreach (int identifier in new[] { 2, 1, 4, 3, 2 })
        {
            session.Key(NativeMethods.VirtualKeyTab);
            await session.WaitForFocusAsync(GetDialogItem(session.Dialog, identifier));
        }
        session.Key(NativeMethods.VirtualKeyEnter);
        Assert.IsNull(await session.ResultAsync());
        session.AssertRestored();
    }

    [TestMethod]
    public async Task 确定按钮原生点击返回文本()
    {
        using PromptSession session = new();
        await session.ReadyAsync();
        _ = NativeMethods.SetWindowText(session.Edit, "42");
        _ = NativeMethods.SendMessage(GetDialogItem(session.Dialog, 1), 0x00F5, 0, 0);
        Assert.AreEqual("42", await session.ResultAsync());
    }

    [TestMethod]
    public async Task 非按钮来源的同名通知不能确认或取消()
    {
        using PromptSession session = new();
        await session.ReadyAsync();
        _ = NativeMethods.SendMessage(session.Dialog, NativeMethods.WindowMessageCommand, 1, session.Edit);
        Assert.IsTrue(NativeMethods.IsWindow(session.Dialog));
        _ = NativeMethods.SendMessage(session.Dialog, NativeMethods.WindowMessageCommand, (1u << 16) | 2, GetDialogItem(session.Dialog, 2));
        Assert.IsTrue(NativeMethods.IsWindow(session.Dialog));
    }

    [TestMethod]
    public async Task 中文组词期间Enter和Esc不关闭输入窗口()
    {
        using PromptSession session = new();
        await session.ReadyAsync();
        _ = NativeMethods.SendMessage(session.Edit, 0x010D, 0, 0);
        session.Key(NativeMethods.VirtualKeyEnter);
        session.Key(NativeMethods.VirtualKeyEscape);
        // 向同一消息队列发送屏障，确认按键已经处理，而非仅检查发送时的窗口。
        await session.DrainAsync();
        Assert.IsTrue(NativeMethods.IsWindow(session.Dialog));
        _ = NativeMethods.SendMessage(session.Edit, 0x010E, 0, 0);
        session.Key(NativeMethods.VirtualKeyEscape);
        Assert.IsNull(await session.ResultAsync());
    }

    [TestMethod]
    public async Task ShiftTab反向循环且关闭按钮Enter取消()
    {
        using PromptSession session = new();
        await session.ReadyAsync();
        await session.DrainAsync(() =>
        {
            byte[] keys = new byte[256];
            Assert.IsTrue(GetKeyboardState(keys));
            keys[NativeMethods.VirtualKeyShift] = 0x80;
            Assert.IsTrue(SetKeyboardState(keys));
        });
        try
        {
            session.Key(NativeMethods.VirtualKeyTab);
            await session.WaitForFocusAsync(GetDialogItem(session.Dialog, 4));
        }
        finally
        {
            await session.DrainAsync(() =>
            {
                byte[] keys = new byte[256];
                Assert.IsTrue(GetKeyboardState(keys));
                keys[NativeMethods.VirtualKeyShift] = 0;
                Assert.IsTrue(SetKeyboardState(keys));
            });
        }
        session.Key(NativeMethods.VirtualKeyEnter);
        Assert.IsNull(await session.ResultAsync());
    }

    [TestMethod]
    public async Task 嵌套消息循环保留主循环退出请求并释放模态资源()
    {
        using PromptSession session = new();
        await session.ReadyAsync();
        await session.DrainAsync(() => NativeMethods.PostQuitMessage(7));
        Assert.IsNull(await session.ResultAsync());
        Assert.AreEqual(7, session.QuitCode);
        session.AssertRestored();
    }

    [TestMethod]
    public async Task Dpi变化更新原控件尺寸字体且保留输入和焦点()
    {
        using IDisposable firstDpi = NativeTheme.PushVisualAuditDpiOverride(96);
        using PromptSession session = new();
        await session.ReadyAsync();
        nint edit = session.Edit;
        _ = NativeMethods.SetWindowText(edit, "123");
        _ = NativeMethods.GetWindowRectangle(edit, out NativeMethods.Rectangle before);
        using IDisposable nextDpi = NativeTheme.PushVisualAuditDpiOverride(144);
        _ = NativeMethods.GetWindowRectangle(session.Dialog, out NativeMethods.Rectangle suggested);
        nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.Rectangle>());
        try
        {
            Marshal.StructureToPtr(suggested, buffer, false);
            _ = NativeMethods.SendMessage(session.Dialog, NativeMethods.WindowMessageDpiChanged, 144u | (144u << 16), buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
        _ = NativeMethods.GetWindowRectangle(edit, out NativeMethods.Rectangle after);
        Assert.AreEqual(edit, session.Edit);
        Assert.AreEqual(edit, session.Focus);
        Assert.AreEqual("123", NativeMethods.GetWindowTextValue(edit));
        Assert.IsGreaterThan(before.Bottom - before.Top, after.Bottom - after.Top);
        Assert.AreEqual(NativeTheme.UiFont, NativeMethods.SendMessage(edit, 0x0031, 0, 0));
        session.Key(NativeMethods.VirtualKeyEscape);
        Assert.IsNull(await session.ResultAsync());
        Assert.IsTrue(session.OwnerDpiRefreshRequested);
        session.AssertRestored();
    }

    [TestMethod]
    [DataRow("70", false, 69)]
    [DataRow("70", true, 0)]
    [DataRow("0", false, 0)]
    [DataRow("2:4", false, 0)]
    public async Task 从真实文档打开输入窗口后只按原有行号规则跳转(string input, bool cancel, int expectedLine)
    {
        using PromptSession session = new(documentMode: true);
        await session.ReadyAsync();
        _ = NativeMethods.SetWindowText(session.Edit, input);
        session.Key(cancel ? NativeMethods.VirtualKeyEscape : NativeMethods.VirtualKeyEnter);
        _ = await session.ResultAsync();
        Assert.AreEqual(expectedLine, session.DocumentLine);
        Assert.IsTrue(session.DocumentReadOnly);
        Assert.IsTrue(session.DocumentUnchanged);
        session.AssertRestored();
    }

    [TestMethod]
    [DataRow(96, false, 13d)]
    [DataRow(96, true, 13d)]
    [DataRow(120, false, 13d)]
    [DataRow(120, true, 13d)]
    [DataRow(144, false, 13d)]
    [DataRow(144, true, 13d)]
    [DataRow(144, false, 40d)]
    [DataRow(144, true, 40d)]
    public async Task 两种主题与不同Dpi字号下输入和按钮完整可见(int dpi, bool dark, double size)
    {
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        using IDisposable dpiScope = NativeTheme.PushVisualAuditDpiOverride(dpi);
        NativeTheme.ConfigureUiTypography(previousFamily, size);
        try
        {
            using PromptSession session = new(dark);
            await session.ReadyAsync();
            Assert.IsTrue(NativeMethods.GetWindowRectangle(session.Dialog, out NativeMethods.Rectangle outer));
            Assert.IsTrue(NativeMethods.GetClientRectangle(session.Dialog, out NativeMethods.Rectangle client));
            Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(400), client.Right);
            NativeMethods.Point origin = new();
            _ = NativeMethods.ClientToScreen(session.Dialog, ref origin);
            foreach (int identifier in new[] { 1, 2, 3, 4 })
            {
                nint control = GetDialogItem(session.Dialog, identifier);
                Assert.AreNotEqual((nint)0, control);
                Assert.AreEqual(NativeTheme.UiFont, NativeMethods.SendMessage(control, 0x0031, 0, 0));
                Assert.IsTrue(NativeMethods.GetWindowRectangle(control, out NativeMethods.Rectangle bounds));
                Assert.IsTrue(bounds.Left >= origin.X && bounds.Right <= origin.X + client.Right);
                Assert.IsTrue(bounds.Top >= origin.Y && bounds.Bottom <= origin.Y + client.Bottom);
                Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale((int)size), bounds.Bottom - bounds.Top, $"控件 {identifier} 的高度不能裁切字体。");
            }
            _ = NativeMethods.GetWindowRectangle(GetDialogItem(session.Dialog, 2), out NativeMethods.Rectangle cancel);
            _ = NativeMethods.GetWindowRectangle(GetDialogItem(session.Dialog, 1), out NativeMethods.Rectangle confirm);
            Assert.IsGreaterThanOrEqualTo(cancel.Right + NativeTheme.Scale(8), confirm.Left, "主要动作必须在右侧。");
            Assert.AreEqual(UiText.LineNumber, NativeAccessibility.GetNameForTest(session.Edit));
            nint close = GetDialogItem(session.Dialog, 4);
            Assert.AreEqual(string.Empty, NativeMethods.GetWindowTextValue(close), "关闭图形不能使用随字号放大的字体字符。");
            Assert.AreEqual(UiText.Close, NativeAccessibility.GetNameForTest(close));
            _ = NativeMethods.GetWindowRectangle(close, out NativeMethods.Rectangle closeBounds);
            Assert.AreEqual(NativeTheme.Scale(32), closeBounds.Right - closeBounds.Left);
            nint dc = NativeMethods.GetDeviceContext(session.Edit);
            try
            {
                nint brush = NativeMethods.SendMessage(session.Dialog, NativeMethods.WindowMessageControlColorEdit, unchecked((nuint)dc), session.Edit);
                Assert.AreNotEqual((nint)0, brush);
                Assert.AreEqual(NativeTheme.Palette(dark).Panel, GetBackgroundColor(dc));
                Assert.AreEqual(NativeTheme.Palette(dark).Text, GetTextColor(dc));
            }
            finally { _ = NativeMethods.ReleaseDeviceContext(session.Edit, dc); }
        }
        finally { NativeTheme.ConfigureUiTypography(previousFamily, previousSize); }
    }

    private sealed class PromptSession : IDisposable
    {
        private const uint BarrierMessage = 0x8049;
        private readonly Thread _thread;
        private readonly TaskCompletionSource<string?> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string _title = $"输入窗口回归 {Guid.NewGuid():N}";
        private readonly NativeMethods.SubclassProcedure _ownerProcedure;
        private TaskCompletionSource? _barrier;
        private Action? _barrierAction;
        private nint _originalFocus;
        private nint _returnedFocus;
        private bool _ownerEnabled;
        private bool _scrimRemoved;
        internal nint Owner { get; private set; }
        internal int? QuitCode { get; private set; }
        internal int DocumentLine { get; private set; }
        internal bool DocumentReadOnly { get; private set; }
        internal bool DocumentUnchanged { get; private set; }
        internal bool OwnerDpiRefreshRequested { get; private set; }
        internal nint Dialog { get; private set; }
        internal nint Edit => GetDialogItem(Dialog, 3);
        internal nint Focus
        {
            get
            {
                uint thread = NativeMethods.GetWindowThreadProcessId(Dialog, out _);
                GuiThreadInfo info = new() { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
                Assert.IsTrue(GetGuiThreadInfo(thread, ref info));
                return info.Focus;
            }
        }

        internal PromptSession(bool dark = false, bool documentMode = false)
        {
            if (documentMode) _title = UiText.GoToLine;
            _ownerProcedure = (window, message, word, parameter, _, _) =>
            {
                if (message == NativeMethods.WindowMessageDpiChanged)
                    OwnerDpiRefreshRequested = !NativeMethods.IsWindow(Dialog);
                if (message == BarrierMessage)
                {
                    try { _barrierAction?.Invoke(); _barrier?.TrySetResult(); }
                    catch (Exception exception) { _barrier?.TrySetException(exception); }
                    return 0;
                }
                return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
            };
            _thread = new(() =>
            {
                Owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, "输入窗口回归宿主",
                    NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible,
                    20, 20, 1400, 900, 0, 0, NativeMethods.GetModuleHandle(null), 0);
                nint document = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, "只读文档",
                    NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible,
                    20, 20, 800, 500, Owner, 0, NativeMethods.GetModuleHandle(null), 0);
                _originalFocus = NativeMethods.CreateWindow(0, NativeMethods.EditClass, "原焦点",
                    NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible,
                    20, 20, 100, 30, document, 0, NativeMethods.GetModuleHandle(null), 0);
                try
                {
                    _ = NativeMethods.SetWindowSubclass(Owner, _ownerProcedure, 1, 0);
                    _ = NativeMethods.SetFocus(_originalFocus);
                    using NativeDocumentView? view = documentMode ? CreateDocument(document) : null;
                    string? result;
                    if (view is null) result = NativeTextPrompt.Show(document, _title, UiText.LineNumber, dark);
                    else
                    {
                        view.SetBounds(0, 0, 800, 500);
                        _originalFocus = GetDialogItem(view.Handle, 100);
                        _ = NativeMethods.SetFocus(_originalFocus);
                        string? original = view.CurrentText;
                        // 通过真实工具栏按钮进入模态消息循环。
                        nint trigger = GetDialogItem(view.Handle, 5);
                        _ = NativeMethods.SendMessage(trigger, 0x00F5, 0, 0);
                        nint position = NativeMethods.SendMessage(_originalFocus, 2008, 0, 0);
                        DocumentLine = (int)NativeMethods.SendMessage(_originalFocus, 2166, unchecked((nuint)position), 0);
                        DocumentReadOnly = view.IsTextReadOnly;
                        DocumentUnchanged = original == view.CurrentText;
                        // BM_CLICK 会先聚焦触发按钮；取消或无效输入应恢复该焦点。
                        if (DocumentLine == 0) _originalFocus = trigger;
                        result = null;
                    }
                    // WM_QUIT 是低优先级队列状态；先排空关闭窗口留下的唤醒和绘制消息。
                    while (PeekMessage(out NativeMethods.Message pending, 0, 0, 0, 1))
                    {
                        if (pending.MessageId == 0x0012) { QuitCode = unchecked((int)pending.WordParameter); break; }
                        _ = NativeMethods.DispatchMessage(ref pending);
                    }
                    _returnedFocus = NativeMethods.GetFocus();
                    _ownerEnabled = NativeMethods.IsWindowEnabled(Owner);
                    _scrimRemoved = FindWindowEx(Owner, 0, "Augit.ModalScrim.Native", null) == 0;
                    _closed.SetResult(result);
                }
                catch (Exception exception) { _closed.TrySetException(exception); }
                finally
                {
                    _ = NativeMethods.RemoveWindowSubclass(Owner, _ownerProcedure, 1);
                    _ = NativeMethods.DestroyWindow(Owner);
                }
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        internal async Task ReadyAsync()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline && !_closed.Task.IsCompleted)
            {
                Dialog = FindWindow("Augit.TextPrompt.Native", _title);
                if (Dialog != 0 && NativeMethods.IsWindowVisible(Dialog) && Edit != 0)
                {
                    await DrainAsync();
                    return;
                }
                await Task.Delay(20);
            }
            if (_closed.Task.IsCompleted) _ = await _closed.Task;
            Assert.Fail("输入窗口没有进入可操作状态。");
        }

        private static NativeDocumentView CreateDocument(nint parent)
        {
            string root = Path.GetTempPath();
            string path = Path.Combine(root, "augit-prompt-test.txt");
            string text = string.Join('\n', Enumerable.Range(1, 100).Select(line => $"只读文本 第 {line} 行"));
            DocumentReadResult result = new(DocumentReadStatus.TextReady, path, path, new(DocumentKind.Text, "UTF-8 文本"),
                System.Text.Encoding.UTF8.GetByteCount(text), text, null, null, string.Empty);
            return new(parent, root, result, new(), _ => { }, (_, _, _) => { });
        }

        internal void Key(int key) => _ = NativeMethods.PostMessage(Focus, NativeMethods.WindowMessageKeyDown, (nuint)key, 0);

        internal async Task DrainAsync(Action? action = null)
        {
            _barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _barrierAction = action;
            _ = NativeMethods.PostMessage(Owner, BarrierMessage, 0, 0);
            await _barrier.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        internal async Task WaitForFocusAsync(nint target)
        {
            await DrainAsync();
            Assert.AreEqual(target, Focus);
        }

        internal async Task<string?> ResultAsync() => await _closed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        internal void AssertRestored()
        {
            Assert.AreEqual(_originalFocus, _returnedFocus);
            Assert.IsTrue(_ownerEnabled);
            Assert.IsTrue(_scrimRemoved);
            Assert.IsFalse(NativeMethods.IsWindow(Dialog));
        }

        public void Dispose()
        {
            if (!_closed.Task.IsCompleted && Dialog != 0 && NativeMethods.IsWindow(Dialog))
                _ = NativeMethods.PostMessage(Dialog, NativeMethods.WindowMessageClose, 0, 0);
            Assert.IsTrue(_thread.Join(TimeSpan.FromSeconds(8)), "输入窗口测试线程没有退出。");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        internal uint Size, Flags;
        internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        internal NativeMethods.Rectangle CaretRectangle;
    }

    [DllImport("user32.dll", EntryPoint = "GetGUIThreadInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGuiThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? title);
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll", EntryPoint = "GetDlgItem")]
    private static extern nint GetDialogItem(nint window, int identifier);
    [DllImport("gdi32.dll", EntryPoint = "GetBkColor")]
    private static extern uint GetBackgroundColor(nint dc);
    [DllImport("gdi32.dll", EntryPoint = "GetTextColor")]
    private static extern uint GetTextColor(nint dc);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] keys);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] keys);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMethods.Message message, nint window, uint minimum, uint maximum, uint flags);
}
