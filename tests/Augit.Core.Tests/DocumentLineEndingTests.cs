using System.Text;
using Augit.Core.Documents;

namespace Augit.Core.Tests;

[TestClass]
public sealed class DocumentLineEndingTests
{
    [TestMethod]
    [DataRow("", DocumentLineEndings.None)]
    [DataRow("中文😀", DocumentLineEndings.None)]
    [DataRow("\n", DocumentLineEndings.Lf)]
    [DataRow("一\n二\n", DocumentLineEndings.Lf)]
    [DataRow("一\r\n二\r\n", DocumentLineEndings.CrLf)]
    [DataRow("一\r二\r", DocumentLineEndings.Cr)]
    [DataRow("一\r\n二\n", DocumentLineEndings.Mixed)]
    [DataRow("一\n二\r\n", DocumentLineEndings.Mixed)]
    [DataRow("一\r二\n", DocumentLineEndings.Mixed)]
    [DataRow("一\r\n\r", DocumentLineEndings.Mixed)]
    [DataRow("\ufeff一\r\n二", DocumentLineEndings.CrLf)]
    public void 换行格式来自完整Utf8内容且不改变输入(string text, DocumentLineEndings expected)
    {
        byte[] content = Encoding.UTF8.GetBytes(text);
        Assert.AreEqual(expected, DocumentLineEndingDetector.Detect(content));
        Assert.AreEqual(text, Encoding.UTF8.GetString(content));
    }
}
