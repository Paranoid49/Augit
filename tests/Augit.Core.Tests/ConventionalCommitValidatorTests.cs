using Augit.Core.Git;

namespace Augit.Core.Tests;

[TestClass]
public sealed class ConventionalCommitValidatorTests
{
    [TestMethod]
    [DataRow("feat: add status panel")]
    [DataRow("fix(core): handle empty repository")]
    [DataRow("改进(界面)!: 调整布局")]
    [DataRow("feat!: break API\n\n说明\n\nBREAKING CHANGE: changed contract")]
    public void 接受规范标题及正文Footer(string message)
    {
        Assert.IsTrue(ConventionalCommitValidator.Validate(message).IsValid);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("missing separator")]
    [DataRow("feat: ")]
    [DataRow("feat: title\nbody without blank line")]
    [DataRow("feat: title\n\nBREAKING CHANGE missing colon")]
    public void 拒绝不符合规范结构的信息(string? message)
    {
        GitCommitValidationResult result = ConventionalCommitValidator.Validate(message);

        Assert.IsFalse(result.IsValid);
        Assert.IsNotNull(result.ErrorMessage);
    }

    [TestMethod]
    public void 不限制Type枚举和标题长度()
    {
        string message = $"custom-type: {new string('长', 300)}";

        Assert.IsTrue(ConventionalCommitValidator.Validate(message).IsValid);
    }
}
