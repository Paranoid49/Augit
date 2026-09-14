using System.Runtime.InteropServices;
using Augit.Core.Git;

namespace Augit.App.Tests;

public sealed partial class NativeConflictResolverInteractionTests
{
    private const string UndoConflict = "<<<<<<< HEAD\n中文😀左\n||||||| base\n祖先\n=======\n中文😀右\n>>>>>>> feature\n";

    [TestMethod]
    [DataRow('Z', true)]
    [DataRow('Y', false)]
    public Task 结果区快捷键消费撤销重做且保存中冻结(char redoKey, bool redoShift) => RunAsync(async (dialog, service) =>
    {
        string initial = dialog.ResultTextForTest!;
        ClickConflictAction(dialog, 3);
        string accepted = dialog.ResultTextForTest!;
        nint editor = dialog.ResultHandleForTest;
        _ = NativeMethods.SetFocus(editor);
        Assert.IsTrue(EditShortcut(dialog, editor, 'Z'));
        Assert.AreEqual(initial, dialog.ResultTextForTest);
        Assert.IsTrue(EditShortcut(dialog, editor, redoKey, redoShift));
        Assert.AreEqual(accepted, dialog.ResultTextForTest);
        Assert.IsFalse(EditShortcut(dialog, editor, 'Z', control: false));
        Assert.IsFalse(EditShortcut(dialog, editor, 'Z', alt: true));
        Task saving = dialog.SaveForTestAsync();
        _ = NativeMethods.SetFocus(editor);
        Assert.IsTrue(EditShortcut(dialog, editor, 'Z'));
        Assert.AreEqual(accepted, dialog.ResultTextForTest, "保存中的快捷键不得修改已交给服务的正文。");
        service.Pending.SetResult(Failure());
        await saving;
        nint side = dialog.YoursHandleForTest;
        _ = NativeMethods.SetFocus(side);
        Assert.IsFalse(EditShortcut(dialog, side, 'Z'));
        Assert.IsFalse(EditShortcut(dialog, editor, 'Z'), "来自非焦点结果区的消息不能撤销正文。");
        Assert.AreEqual(accepted, dialog.ResultTextForTest);
        _ = NativeMethods.SetFocus(editor);
        Assert.IsTrue(EditShortcut(dialog, editor, 'Z'));
        Assert.AreEqual(initial, dialog.ResultTextForTest);
    }, unresolved: true);

    [TestMethod]
    [DataRow(1, "中文😀左\n", false)]
    [DataRow(2, "中文😀右\n", false)]
    [DataRow(3, "中文😀左\n中文😀右\n", false)]
    [DataRow(1, "中文😀左\n", true)]
    [DataRow(2, "中文😀右\n", true)]
    [DataRow(3, "中文😀左\n中文😀右\n", true)]
    public Task 三种接受独立撤销重做且保留先前手工编辑(int command, string accepted, bool crlf)
    {
        string eol = crlf ? "\r\n" : "\n";
        string initial = ("前文😀\n" + UndoConflict + "后文😀\n").Replace("\n", eol, StringComparison.Ordinal);
        return RunAsync(async (dialog, service) =>
        {
            nint editor = dialog.ResultHandleForTest;
            Assert.IsFalse(dialog.ResultIsDirtyForTest);
            string edit = "手工补充😀" + eol;
            Append(editor, edit);
            string edited = initial + edit;
            ClickConflictAction(dialog, command);
            string resolved = "前文😀" + eol + accepted.Replace("\n", eol, StringComparison.Ordinal) + "后文😀" + eol + edit;
            Assert.AreEqual(resolved, dialog.ResultTextForTest);
            Assert.IsTrue(dialog.ResultIsDirtyForTest);
            Assert.IsFalse(dialog.ResultPresentationPendingForTest, "同步接受已更新高亮，不应留下重复的延迟解析。");
            Assert.IsTrue(dialog.YoursIsReadOnlyForTest && dialog.TheirsIsReadOnlyForTest);
            Assert.IsFalse(dialog.ResultIsReadOnlyForTest);
            Assert.IsFalse(NativeMethods.IsWindowEnabled(Item(dialog, 1)));

            _ = NativeMethods.SendMessage(editor, 2176, 0, 0);
            Assert.AreEqual(edited, dialog.ResultTextForTest, "一步撤销仅恢复当前冲突，不能连同先前手工输入一起撤销。");
            Assert.IsTrue(dialog.ResultIsDirtyForTest);
            await WaitForConflictPresentationAsync(dialog);
            Assert.Contains("未处理冲突块：1", dialog.NoticeForTest);
            for (int id = 1; id <= 5; id++) Assert.IsTrue(NativeMethods.IsWindowEnabled(Item(dialog, id)));
            await dialog.SaveForTestAsync();
            Assert.IsEmpty(service.Saves, "撤销恢复冲突后不能提交未解决的结果。");

            _ = NativeMethods.SendMessage(editor, 2176, 0, 0);
            Assert.AreEqual(initial, dialog.ResultTextForTest);
            Assert.IsFalse(dialog.ResultIsDirtyForTest, "全部撤销回保存点后不能继续误报未保存。");
            _ = NativeMethods.SendMessage(editor, 2011, 0, 0);
            Assert.AreEqual(edited, dialog.ResultTextForTest);
            _ = NativeMethods.SendMessage(editor, 2011, 0, 0);
            Assert.AreEqual(resolved, dialog.ResultTextForTest);
            await WaitForConflictPresentationAsync(dialog);
            Assert.IsTrue(dialog.ResultIsDirtyForTest);
            for (int id = 1; id <= 5; id++) Assert.IsFalse(NativeMethods.IsWindowEnabled(Item(dialog, id)));
            Task saving = dialog.SaveForTestAsync();
            Assert.AreEqual(resolved, service.Saves.Single().ResultText);
            service.Pending.SetResult(Failure());
            await saving;
            _ = NativeMethods.SendMessage(editor, 2176, 0, 0);
            Assert.AreEqual(edited, dialog.ResultTextForTest, "保存失败后仍可撤销接受动作。");
        }, resultText: initial);
    }

    [TestMethod]
    public Task 多块连续接受逐步撤销重做并恢复原保存点() => RunAsync(async (dialog, service) =>
    {
        string original = dialog.ResultTextForTest!;
        ClickConflictAction(dialog, 1);
        string first = "前文😀\n中文😀左\n中间\n" + UndoConflict;
        Assert.AreEqual(first, dialog.ResultTextForTest);
        ClickConflictAction(dialog, 2);
        string second = "前文😀\n中文😀左\n中间\n中文😀右\n";
        Assert.AreEqual(second, dialog.ResultTextForTest);
        _ = NativeMethods.SendMessage(dialog.ResultHandleForTest, 2176, 0, 0);
        Assert.AreEqual(first, dialog.ResultTextForTest);
        _ = NativeMethods.SendMessage(dialog.ResultHandleForTest, 2176, 0, 0);
        Assert.AreEqual(original, dialog.ResultTextForTest);
        Assert.IsFalse(dialog.ResultIsDirtyForTest);
        await WaitForConflictPresentationAsync(dialog);
        Assert.Contains("未处理冲突块：2", dialog.NoticeForTest);
        _ = NativeMethods.SendMessage(dialog.ResultHandleForTest, 2011, 0, 0);
        Assert.AreEqual(first, dialog.ResultTextForTest);
        _ = NativeMethods.SendMessage(dialog.ResultHandleForTest, 2011, 0, 0);
        Assert.AreEqual(second, dialog.ResultTextForTest);
        await WaitForConflictPresentationAsync(dialog);
        Assert.IsEmpty(GitConflictText.Parse(dialog.ResultTextForTest!));
        Assert.IsEmpty(service.Saves);
    }, resultText: "前文😀\n" + UndoConflict + "中间\n" + UndoConflict);

    [TestMethod]
    public Task 撤销接受后可直接关闭而不误报未保存() => RunAsync(async (dialog, service) =>
    {
        ClickConflictAction(dialog, 3);
        _ = NativeMethods.SendMessage(dialog.ResultHandleForTest, 2176, 0, 0);
        Assert.IsFalse(dialog.ResultIsDirtyForTest);
        await WaitForConflictPresentationAsync(dialog);
        _ = NativeMethods.SendMessage(dialog.HandleForTest, NativeMethods.WindowMessageClose, 0, 0);
        Assert.AreEqual((nint)0, dialog.HandleForTest);
        Assert.IsEmpty(service.Saves);
    }, unresolved: true);

    [TestMethod]
    public Task 保留外部版本后撤销到旧保存点仍需保护正文() => RunAsync(async (dialog, service) =>
    {
        string original = dialog.ResultTextForTest!;
        ClickConflictAction(dialog, 3);
        Task reading = dialog.ReloadForTestAsync();
        Task choosing = ChooseReloadAsync(dialog.HandleForTest, reload: false);
        service.Reloads.Single().Completion.SetResult(service.Reloads.Single().Changed("外部新版本\n"));
        await reading;
        await choosing;
        _ = NativeMethods.SendMessage(dialog.ResultHandleForTest, 2176, 0, 0);
        Assert.AreEqual(original, dialog.ResultTextForTest);
        Assert.AreEqual((nint)0, NativeMethods.SendMessage(dialog.ResultHandleForTest, 2159, 0, 0));
        Assert.IsTrue(dialog.ResultIsDirtyForTest, "旧保存点与用户刚确认的外部版本不同，不能免去后续保护。");
        reading = dialog.ReloadForTestAsync();
        choosing = ChooseReloadAsync(dialog.HandleForTest, reload: true);
        service.Reloads[1].Completion.SetResult(service.Reloads[1].Changed("再次外部更新\n", "external-2"));
        await reading;
        await choosing;
        Assert.AreEqual("再次外部更新\n", dialog.ResultTextForTest);
        Assert.IsFalse(dialog.ResultIsDirtyForTest);
        Assert.AreEqual((nint)0, NativeMethods.SendMessage(dialog.ResultHandleForTest, 2174, 0, 0), "明确重新载入后不能撤销恢复旧磁盘版本。");
    }, unresolved: true);

    private static void ClickConflictAction(NativeConflictResolverDialog dialog, int command) =>
        _ = NativeMethods.SendMessage(Item(dialog, command), 0x00F5, 0, 0);

    private static bool EditShortcut(NativeConflictResolverDialog dialog, nint editor, char key,
        bool shift = false, bool control = true, bool alt = false)
    {
        byte[] previous = new byte[256];
        Assert.IsTrue(GetKeyboardState(previous));
        byte[] keys = new byte[256];
        keys[NativeMethods.VirtualKeyControl] = control ? (byte)0x80 : (byte)0;
        keys[NativeMethods.VirtualKeyShift] = shift ? (byte)0x80 : (byte)0;
        keys[0x12] = alt ? (byte)0x80 : (byte)0;
        Assert.IsTrue(SetKeyboardState(keys));
        try
        {
            return dialog.HandleResultEditShortcut(new()
            {
                Window = editor,
                MessageId = NativeMethods.WindowMessageKeyDown,
                WordParameter = key,
            });
        }
        finally { Assert.IsTrue(SetKeyboardState(previous)); }
    }

    private static async Task WaitForConflictPresentationAsync(NativeConflictResolverDialog dialog)
    {
        long deadline = Environment.TickCount64 + 5_000;
        while (dialog.ResultPresentationPendingForTest && Environment.TickCount64 < deadline) await Task.Delay(10);
        Assert.IsFalse(dialog.ResultPresentationPendingForTest, "正文变更后的冲突高亮未完成更新。");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] state);
}
