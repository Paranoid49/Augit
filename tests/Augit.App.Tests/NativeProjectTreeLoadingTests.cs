using System.Diagnostics;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Files;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeProjectTreeLoadingTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 增删只修改目标节点并保留展开选择与正文(bool manual) => RunAsync(null, async (window, workspace) =>
    {
        string nested = Path.Combine(workspace, "目录", "深层");
        string leaf = Path.Combine(nested, "保留.txt");
        await window.ExpandTreePathForTestAsync(nested);
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "README.txt"));
        Assert.IsTrue(window.SelectTreePathForTest(leaf));
        nint focus = NativeMethods.GetFocus();
        nint document = window.ActiveDocumentViewForTest!.Handle;
        string[] preserved = [workspace, Path.Combine(workspace, "目录"), nested, leaf, Path.Combine(workspace, "文件1.txt"), Path.Combine(workspace, "文件3.txt")];
        nint[] handles = preserved.Select(window.TreeItemForTest).ToArray();
        int layouts = window.LayoutInvocationCountForTest;
        string added = Path.Combine(workspace, "文件2.txt"), removed = Path.Combine(workspace, "文件20.txt");
        File.WriteAllText(added, "新增");
        File.Delete(removed);
        if (manual) await window.RefreshWorkspaceForTestAsync();
        else await WaitAsync(() => window.TreeItemForTest(added) != 0 && window.TreeItemForTest(removed) == 0);
        CollectionAssert.AreEqual(handles, preserved.Select(window.TreeItemForTest).ToArray(), "未变化项及已展开子树必须保留原生身份。");
        Assert.AreEqual(leaf, window.SelectedTreePathForTest);
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        Assert.AreEqual(document, window.ActiveDocumentViewForTest.Handle);
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.IsTrue(IsExpanded(window, nested));
        nint[] expected = WorkspaceDirectoryService.EnumerateChildren(workspace).Select(entry => window.TreeItemForTest(entry.FullPath)).ToArray();
        CollectionAssert.AreEqual(expected, Children(window, workspace), "界面顺序必须采用目录服务的 Windows 自然排序。");
        await window.RefreshWorkspaceForTestAsync();
        CollectionAssert.AreEqual(handles, preserved.Select(window.TreeItemForTest).ToArray(), "无变化刷新仍保留原生节点。");
    });

    [TestMethod]
    public Task 删除选中项选择同组最近邻且目录类型变化正确替换() => RunAsync(
        workspace => File.Delete(Path.Combine(workspace, "README.txt")), async (window, workspace) =>
    {
        string first = Path.Combine(workspace, "文件1.txt"), removed = Path.Combine(workspace, "文件3.txt"), next = Path.Combine(workspace, "文件20.txt");
        Assert.IsTrue(window.SelectTreePathForTest(removed));
        nint firstHandle = window.TreeItemForTest(first), nextHandle = window.TreeItemForTest(next);
        File.Delete(removed);
        await window.RefreshWorkspaceForTestAsync();
        Assert.AreEqual(next, window.SelectedTreePathForTest);
        Assert.AreEqual(nextHandle, window.TreeItemForTest(next));
        File.Delete(next);
        await window.RefreshWorkspaceForTestAsync();
        Assert.AreEqual(first, window.SelectedTreePathForTest);
        File.Delete(removed); // 重复删除不存在的文件不影响其余节点。
        Directory.CreateDirectory(removed);
        File.WriteAllText(Path.Combine(removed, "新子项.txt"), "正文");
        await window.RefreshWorkspaceForTestAsync();
        await window.ExpandTreePathForTestAsync(removed);
        Assert.AreNotEqual((nint)0, window.TreeItemForTest(Path.Combine(removed, "新子项.txt")));
        Assert.AreEqual(firstHandle, window.TreeItemForTest(first));
        nint[] ordered = Children(window, workspace);
        Assert.IsLessThan(Array.IndexOf(ordered, firstHandle),
            Array.IndexOf(ordered, window.TreeItemForTest(removed)), "新目录必须排在文件前。");
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 分批加载期间用户操作和工作区切换不会被旧结果覆盖(bool switchWorkspace) => RunAsync(workspace =>
    {
        CreateFiles(Path.Combine(workspace, "大目录"), 10000);
        string second = Path.Combine(workspace, "第二工作区");
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(second, "新文件.txt"), "正文");
    }, async (window, workspace) =>
    {
        string large = Path.Combine(workspace, "大目录"), selected = Path.Combine(workspace, "README.txt");
        Task<bool>? switching = null;
        int yields = 0;
        window.TreeLoadYieldedForTest = () =>
        {
            yields++;
            window.TreeLoadYieldedForTest = null;
            if (switchWorkspace) switching = window.OpenWorkspaceAsync(Path.Combine(workspace, "第二工作区"));
            else
            {
                Assert.IsTrue(window.SelectTreePathForTest(selected));
                _ = NativeMethods.SendMessage(window.FileTreeHandleForTest, NativeMethods.TreeViewExpand,
                    NativeMethods.TreeViewCollapseItem, window.TreeItemForTest(large));
                Assert.IsFalse(IsExpanded(window, large), "收起命令必须先在原生控件上生效。");
            }
        };
        await window.ExpandTreePathForTestAsync(large);
        Assert.IsGreaterThan(0, yields, "大目录须分批返回消息循环。");
        if (switchWorkspace)
        {
            Assert.IsNotNull(switching);
            Assert.IsTrue(await switching);
            Assert.AreEqual(Path.Combine(workspace, "第二工作区"), window.WorkspaceRoot);
            Assert.AreEqual((nint)0, window.TreeItemForTest(large));
            Assert.AreNotEqual((nint)0, window.TreeItemForTest(Path.Combine(workspace, "第二工作区", "新文件.txt")));
        }
        else
        {
            Assert.AreEqual(selected, window.SelectedTreePathForTest);
            Assert.IsFalse(IsExpanded(window, large), "加载完成不能重新展开用户主动收起的目录。");
            Assert.HasCount(10000, Children(window, large));
        }
    });

    [TestMethod]
    public Task 十万文件按需加载并在千项展开及增量刷新期间保持消息响应() => RunAsync(workspace =>
    {
        Parallel.For(0, 100, new ParallelOptions { MaxDegreeOfParallelism = 4 }, index =>
            CreateFiles(Path.Combine(workspace, $"批次{index:000}"), 1000));
    }, async (window, workspace) =>
    {
        Assert.IsLessThan(120, window.LoadedTreeNodeCountForTest, "启动不能枚举未展开目录中的十万文件。");
        string directory = Path.Combine(workspace, "批次050");
        await MeasureAsync(window, "千项目录首次展开", () => window.ExpandTreePathForTestAsync(directory));
        Assert.HasCount(1000, Children(window, directory));
        Assert.IsLessThan(1120, window.LoadedTreeNodeCountForTest);
        string selected = Path.Combine(directory, "文件0500.txt");
        Assert.IsTrue(window.SelectTreePathForTest(selected));
        nint identity = window.TreeItemForTest(selected);
        int layouts = window.LayoutInvocationCountForTest;
        File.WriteAllText(Path.Combine(directory, "文件1001.txt"), "新增");
        await MeasureAsync(window, "千项目录增量刷新", window.RefreshWorkspaceForTestAsync);
        Assert.HasCount(1001, Children(window, directory));
        Assert.AreEqual(identity, window.TreeItemForTest(selected));
        Assert.AreEqual(selected, window.SelectedTreePathForTest);
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
    });

    private static async Task MeasureAsync(MainWindow window, string label, Func<Task> operation)
    {
        using CancellationTokenSource stop = new();
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(int Count, int Timeouts, double Maximum)> probe = Task.Run(async () =>
        {
            int count = 0, timeouts = 0;
            double maximum = 0;
            ready.SetResult();
            do
            {
                Stopwatch response = Stopwatch.StartNew();
                if (SendMessageTimeout(window.Handle, 0, 0, 0, 2, 250, out _) == 0) timeouts++;
                maximum = Math.Max(maximum, response.Elapsed.TotalMilliseconds);
                count++;
                await Task.Delay(5);
            } while (!stop.IsCancellationRequested);
            return (count, timeouts, maximum);
        });
        await ready.Task;
        Stopwatch duration = Stopwatch.StartNew();
        (int Count, int Timeouts, double Maximum) result;
        try { await operation(); }
        finally { stop.Cancel(); result = await probe; }
        Console.WriteLine($"{label}：完成 {duration.Elapsed.TotalMilliseconds:F2}ms，探针 {result.Count} 次，最大 {result.Maximum:F2}ms，250ms 超时 {result.Timeouts} 次；此为测试宿主诊断，不是发布版冷启动验收。");
        Assert.AreEqual(0, result.Timeouts);
    }

    private static async Task RunAsync(Action<string>? prepare, Func<MainWindow, string, Task> verify)
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace"), nested = Path.Combine(workspace, "目录", "深层");
        Directory.CreateDirectory(nested);
        foreach (string name in new[] { "README.txt", "文件1.txt", "文件3.txt", "文件20.txt" })
            await File.WriteAllTextAsync(Path.Combine(workspace, name), "正文");
        await File.WriteAllTextAsync(Path.Combine(nested, "保留.txt"), "正文");
        if (prepare is not null) await Task.Run(() => prepare(workspace));
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? active = null;
        Thread thread = new(() =>
        {
            Exception? failure = null;
            try
            {
                using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")),
                    new() { Theme = "Light", Window = new() { Width = 1024, Height = 640 } });
                Volatile.Write(ref active, window);
                window.Show();
                window.Post(() => _ = VerifyAsync());
                _ = MainWindow.RunMessageLoop();
                async Task VerifyAsync()
                {
                    try
                    {
                        Assert.IsTrue(await window.OpenWorkspaceAsync(workspace));
                        await window.ExpandTreePathForTestAsync(workspace);
                        await verify(window, workspace);
                    }
                    catch (Exception exception) { failure = exception; }
                    finally { window.Close(); }
                }
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                Volatile.Write(ref active, null);
                if (failure is null) completion.TrySetResult();
                else completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(60)); }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref active);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "目录刷新测试窗口未退出。");
        }
    }

    private static void CreateFiles(string directory, int count)
    {
        Directory.CreateDirectory(directory);
        for (int index = 0; index < count; index++) File.WriteAllBytes(Path.Combine(directory, $"文件{index:0000}.txt"), []);
    }

    private static nint[] Children(MainWindow window, string path)
    {
        List<nint> children = [];
        nint item = NativeMethods.SendMessage(window.FileTreeHandleForTest, NativeMethods.TreeViewGetNextItem,
            NativeMethods.TreeViewChild, window.TreeItemForTest(path));
        while (item != 0)
        {
            children.Add(item);
            item = NativeMethods.SendMessage(window.FileTreeHandleForTest, NativeMethods.TreeViewGetNextItem, 1, item);
        }
        return children.ToArray();
    }

    private static bool IsExpanded(MainWindow window, string path)
    {
        NativeMethods.TreeViewItem item = new()
        {
            Mask = NativeMethods.TreeViewItemState,
            Item = window.TreeItemForTest(path),
            StateMask = NativeMethods.TreeViewStateExpanded
        };
        _ = NativeMethods.SendMessage(window.FileTreeHandleForTest, NativeMethods.TreeViewGetItem, 0, ref item);
        return (item.State & NativeMethods.TreeViewStateExpanded) != 0;
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        Stopwatch deadline = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.IsLessThan(3000, deadline.ElapsedMilliseconds, "外部目录事件没有完成增量刷新。");
            await Task.Delay(10);
        }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wordParameter, nint longParameter,
        uint flags, uint timeout, out nuint result);
}
