using System.Runtime.InteropServices;
using System.Text;
using Augit.Core.Git;

namespace Augit.App.Tests;

public sealed partial class NativeConflictResolverInteractionTests
{
    [TestMethod]
    public Task 样式装饰不触发正文解析而真实插入和撤销仍会更新() => RunAsync(async (dialog, service) =>
    {
        Assert.IsFalse(dialog.ResultPresentationPendingForTest);
        nint editor = dialog.ResultHandleForTest;
        _ = NativeMethods.SendMessage(editor, 2032, 0, 0);
        _ = NativeMethods.SendMessage(editor, 2033, 3, 1);
        _ = NativeMethods.SendMessage(editor, 2500, 8, 0);
        _ = NativeMethods.SendMessage(editor, 2504, 0, 3);
        Assert.IsFalse(dialog.ResultPresentationPendingForTest, "语法样式和变化标记不能触发正文读取、冲突解析和下一轮装饰。");
        Append(editor, "继续编辑😀");
        Assert.IsTrue(dialog.ResultPresentationPendingForTest);
        await Task.Delay(180);
        Assert.IsFalse(dialog.ResultPresentationPendingForTest);
        _ = NativeMethods.SendMessage(editor, 2176, 0, 0);
        Assert.IsTrue(dialog.ResultPresentationPendingForTest);
        await Task.Delay(180);
        Assert.IsFalse(dialog.ResultPresentationPendingForTest);
    }, unresolved: true);

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public Task 保存开始后旧外部读取不能覆盖保存结果(bool saveSucceeds, bool reloadThrows) => RunAsync(async (dialog, service) =>
    {
        Task reading = dialog.ReloadForTestAsync();
        ReloadRequest request = service.Reloads.Single();
        string text = dialog.ResultTextForTest!;
        Task saving = dialog.SaveForTestAsync();
        Assert.IsTrue(request.Token.IsCancellationRequested);
        service.Pending.SetResult(saveSucceeds ? Success() : Failure());
        await saving;
        if (reloadThrows) request.Completion.SetException(new IOException("过期读取失败"));
        else request.Completion.SetResult(request.Changed("过期外部内容"));
        await reading;
        Assert.AreEqual(saveSucceeds, dialog.SavedForTest);
        if (!saveSucceeds)
        {
            Assert.AreEqual(text, dialog.ResultTextForTest);
            Assert.AreEqual("模拟写入失败", dialog.NoticeForTest);
            Task fresh = dialog.ReloadForTestAsync();
            Assert.HasCount(2, service.Reloads);
            service.Reloads[1].Completion.SetResult(service.Reloads[1].Changed("后续真正的外部更新"));
            await fresh;
            Assert.AreEqual("后续真正的外部更新", dialog.ResultTextForTest);
        }
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 销毁取消外部读取并丢弃晚到结果(bool throws) => RunAsync(async (dialog, service) =>
    {
        Task reading = dialog.ReloadForTestAsync();
        ReloadRequest request = service.Reloads.Single();
        dialog.Dispose();
        Assert.IsTrue(request.Token.IsCancellationRequested);
        if (throws) request.Completion.SetException(new OperationCanceledException(request.Token));
        else request.Completion.SetResult(request.Changed("晚到结果"));
        await reading;
        Assert.AreEqual((nint)0, dialog.HandleForTest);
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 读取期间新编辑必须在实际确认窗口选择且保留路径不清除撤销(bool reload) => RunAsync(async (dialog, service) =>
    {
        Task reading = dialog.ReloadForTestAsync();
        ReloadRequest request = service.Reloads.Single();
        string original = dialog.ResultTextForTest!;
        Append(dialog.ResultHandleForTest, "读取期间的新编辑😀\n");
        string edited = dialog.ResultTextForTest!;
        Assert.AreNotEqual(original, edited);
        Task choosing = ChooseReloadAsync(dialog.HandleForTest, reload);
        request.Completion.SetResult(request.Changed("外部内容\n"));
        await reading;
        await choosing;
        Assert.AreEqual(reload ? "外部内容\n" : edited, dialog.ResultTextForTest);
        await Task.Delay(150);
        Assert.Contains(reload ? "已重新载入" : "已保留当前未保存内容", dialog.NoticeForTest);
        if (!reload)
        {
            Assert.AreNotEqual((nint)0, NativeMethods.SendMessage(dialog.ResultHandleForTest, 2174, 0, 0), "保留当前内容不能清空撤销栈。");
            _ = NativeMethods.SendMessage(dialog.ResultHandleForTest, 2176, 0, 0);
            Assert.AreEqual(original, dialog.ResultTextForTest);
        }
        Task saving = dialog.SaveForTestAsync();
        Assert.AreEqual("external", service.Saves.Single().ExpectedVersion.Sha256);
        service.Pending.SetResult(Failure());
        await saving;
    });

    [TestMethod]
    public Task 读取期间再次通知丢弃中间版本并合并为一次补读() => RunAsync(async (dialog, service) =>
    {
        string original = dialog.ResultTextForTest!;
        Task reading = dialog.ReloadForTestAsync();
        dialog.NotifyExternalChangeForTest();
        dialog.NotifyExternalChangeForTest();
        service.Reloads[0].Completion.SetResult(service.Reloads[0].Changed("中间版本"));
        long deadline = Environment.TickCount64 + 5_000;
        while (service.Reloads.Count < 2 && Environment.TickCount64 < deadline) await Task.Delay(5);
        Assert.HasCount(2, service.Reloads);
        Assert.AreEqual(original, dialog.ResultTextForTest, "不能先显示已知过期的中间版本。");
        service.Reloads[1].Completion.SetResult(service.Reloads[1].Changed("最新版本"));
        await reading;
        await Task.Delay(30);
        Assert.HasCount(2, service.Reloads);
        Assert.AreEqual("最新版本", dialog.ResultTextForTest);
    });

    [TestMethod]
    public Task 相同磁盘快照不覆盖未保存正文或打开确认() => RunAsync(async (dialog, service) =>
    {
        Append(dialog.ResultHandleForTest, "保留这段编辑\n");
        string edited = dialog.ResultTextForTest!;
        Task reading = dialog.ReloadForTestAsync();
        ReloadRequest request = service.Reloads.Single();
        request.Completion.SetResult(GitConflictLoadResult.Success(request.Previous));
        await reading;
        Assert.AreEqual(edited, dialog.ResultTextForTest);
    });

    private static void Append(nint editor, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text + '\0');
        nint buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            _ = NativeMethods.SendMessage(editor, 2282, (nuint)(bytes.Length - 1), buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static Task ChooseReloadAsync(nint owner, bool reload) => Task.Run(async () =>
    {
        long deadline = Environment.TickCount64 + 5_000;
        while (Environment.TickCount64 < deadline)
        {
            nint prompt = FindWindow("Augit.ActionConfirmationDialog.Native", "重新载入冲突文件");
            if (prompt != 0 && GetOwnerWindow(prompt, 4) == owner)
            {
                int command = reload ? 10 : 11;
                _ = NativeMethods.PostMessage(prompt, NativeMethods.WindowMessageCommand, (nuint)command, GetDlgItem(prompt, command));
                return;
            }
            await Task.Delay(5);
        }
        Assert.Fail("读取期间编辑后没有显示重新载入或保留的实际确认窗口。");
    });

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string title);

    [DllImport("user32.dll", EntryPoint = "GetWindow")]
    private static extern nint GetOwnerWindow(nint window, uint command);
}
