using Augit.Core.Git;

namespace Augit.App.Tests;

[TestClass]
public sealed class NativeRemoteDialogTests
{
    private static readonly string[] ExpectedFieldLabels = ["名称", "获取 URL", "推送 URL"];
    private static readonly string[] ExpectedDisplayOrder = ["origin", "backup", "upstream"];
    private static readonly int[] ExpectedExistingTabOrder = [1, 20, 21, 22, 12, 11, 15, 17, 10, 16, 13];
    private static readonly int[] ExpectedNewTabOrder = [20, 21, 22, 11, 15, 17, 10, 13, 1];

    [TestMethod]
    public async Task 远端管理视觉稿保留双栏和三个远端字段()
    {
        string path = FindRepositoryFile("docs", "ux-mockups", "mockup.js");
        string source = await File.ReadAllTextAsync(path);
        StringAssert.Contains(source, "managementPage(\"remote\")");
        StringAssert.Contains(source, "名称");
        StringAssert.Contains(source, "获取 URL");
        StringAssert.Contains(source, "推送 URL");
        StringAssert.Contains(source, "dialog(\"远端管理\"");
    }

    [TestMethod]
    public void 远端管理使用宽双栏和三个固定字段()
    {
        (int width, int height, int header, int footer, int sidebar) =
            NativeRemoteDialog.LogicalLayoutForTest;

        Assert.AreEqual((930, 407, 45, 53, 260), (width, height, header, footer, sidebar));
        CollectionAssert.AreEqual(
            ExpectedFieldLabels,
            NativeRemoteDialog.FieldLabelsForTest.ToArray());
    }

    [TestMethod]
    public void 远端管理两种状态具有完整键盘循环()
    {
        CollectionAssert.AreEqual(
            ExpectedExistingTabOrder,
            NativeRemoteDialog.ExistingRemoteTabOrderForTest.ToArray());
        CollectionAssert.AreEqual(
            ExpectedNewTabOrder,
            NativeRemoteDialog.NewRemoteTabOrderForTest.ToArray());
    }

    [TestMethod]
    public void 已有远端始终显示真实推送地址()
    {
        GitRemoteInfo same = new(
            "origin",
            "https://example.com/team/Augit.git",
            "https://example.com/team/Augit.git");
        GitRemoteInfo different = new(
            "backup",
            "https://example.com/team/Augit.git",
            "ssh://git@example.com/team/Augit.git");

        Assert.AreEqual(same.PushUrl, NativeRemoteDialog.DisplayPushUrlForTest(same));
        Assert.AreEqual(different.PushUrl, NativeRemoteDialog.DisplayPushUrlForTest(different));
    }

    [TestMethod]
    public void 远端列表优先显示Origin并稳定排序其余项()
    {
        GitRemoteInfo[] remotes =
        [
            new("upstream", "u", "u"),
            new("backup", "b", "b"),
            new("origin", "o", "o"),
        ];

        IReadOnlyList<GitRemoteInfo> ordered =
            NativeRemoteDialog.OrderRemotesForDisplayForTest(remotes);

        CollectionAssert.AreEqual(
            ExpectedDisplayOrder,
            ordered.Select(remote => remote.Name).ToArray());
    }

    [TestMethod]
    public void 远端管理状态文字保持中文一致性()
    {
        Assert.AreEqual("2 个远端", UiText.RemoteCount(2));
        Assert.AreEqual("确定删除远端 origin 吗？", UiText.ConfirmDeleteRemote("origin"));
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"未找到仓库文件：{Path.Combine(relativeParts)}");
    }
}
