using Augit.Shell;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellFontCatalogTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    public void 返回已安装字体并去除竖排别名及复用缓存()
    {
        IReadOnlyList<string> families = ShellFontCatalog.Families;
        Assert.IsNotEmpty(families);
        Assert.IsTrue(families.Any(name => name.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(families.All(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith('@')));
        Assert.AreEqual(families.Count, families.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.AreSame(families, ShellFontCatalog.Families);
        TestContext.WriteLine($"本机 JetBrains Mono 已安装：{families.Contains("JetBrains Mono", StringComparer.OrdinalIgnoreCase)}；Cascadia Mono 已安装：{families.Contains("Cascadia Mono", StringComparer.OrdinalIgnoreCase)}；Consolas 已安装：{families.Contains("Consolas", StringComparer.OrdinalIgnoreCase)}。");
    }
}
