using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitOutputSanitizerTests
{
    [TestMethod]
    public void 隐藏网址凭据和常见敏感字段()
    {
        const string Output = "https://alice:secret@example.com/repo.git access_token=abc Authorization: Bearer xyz password: pass";

        string sanitized = GitOutputSanitizer.Sanitize(Output);

        Assert.AreEqual("https://***@example.com/repo.git access_token=*** Authorization: *** password: ***", sanitized);
        Assert.DoesNotContain("secret", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("xyz", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("password: pass", sanitized, StringComparison.Ordinal);
    }
}
