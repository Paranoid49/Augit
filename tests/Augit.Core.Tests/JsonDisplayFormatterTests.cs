using Augit.Core.Documents;

namespace Augit.Core.Tests;

[TestClass]
public sealed class JsonDisplayFormatterTests
{
    [TestMethod]
    public void 使用两空格并保持属性顺序()
    {
        JsonDisplayResult result = JsonDisplayFormatter.Format("{\"后\":1,\"前\":2}");

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("{\n  \"后\": 1,\n  \"前\": 2\n}", result.DisplayText.ReplaceLineEndings("\n"));
    }

    [TestMethod]
    [DataRow("{\"a\":1,}")]
    [DataRow("{//注释\n\"a\":1}")]
    public void 拒绝非严格Json并保留原文(string source)
    {
        JsonDisplayResult result = JsonDisplayFormatter.Format(source);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(source, result.DisplayText);
        Assert.IsNotNull(result.ErrorLine);
        Assert.IsNotNull(result.ErrorColumn);
    }
}
