using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// 「选择目录…」的决策（视觉稿「打开工作区」页）。
/// 系统对话框本身无法在无人值守环境弹出，因此把它注入成委托，验证取消 / 无效 / 可用三条路径。
/// </summary>
[TestClass]
public sealed class WorkspacePickerTests
{
    [TestMethod]
    public void 取消时不产生路径()
    {
        WorkspacePickResult result = WorkspacePicker.Pick(_ => null);

        Assert.AreEqual(WorkspacePickOutcome.Cancelled, result.Outcome);
        Assert.IsNull(result.Path);
    }

    [TestMethod]
    public void 选到不存在的目录时给出原因()
    {
        string missing = Path.Combine(Path.GetTempPath(), "Augit.Tests", "gone-" + Guid.NewGuid().ToString("N"));
        WorkspacePickResult result = WorkspacePicker.Pick(_ => missing);

        Assert.AreEqual(WorkspacePickOutcome.Invalid, result.Outcome);
        Assert.IsNull(result.Path);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Reason));
    }

    [TestMethod]
    public void 选到可用目录时返回完整路径()
    {
        using TemporaryDirectory directory = new();
        // 对话框可能返回带结尾分隔符的路径：结果必须是规范化后的完整路径。
        WorkspacePickResult result = WorkspacePicker.Pick(_ => directory.FullPath + Path.DirectorySeparatorChar);

        Assert.AreEqual(WorkspacePickOutcome.Picked, result.Outcome);
        Assert.AreEqual(Path.GetFullPath(directory.FullPath), result.Path);
    }
}
