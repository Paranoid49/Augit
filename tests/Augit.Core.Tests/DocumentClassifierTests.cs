using System.Text;
using Augit.Core.Documents;

namespace Augit.Core.Tests;

[TestClass]
public sealed class DocumentClassifierTests
{
    [TestMethod]
    public void 按签名优先识别受支持图片()
    {
        byte[] content = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        DocumentClassification result = DocumentClassifier.Classify("错误扩展名.txt", content);

        Assert.AreEqual(DocumentKind.Png, result.Kind);
        Assert.IsTrue(result.IsSupportedImage);
    }

    [TestMethod]
    [DataRow("sample.gif", "GIF89a", DocumentKind.Gif)]
    [DataRow("sample.webp", "RIFF0000WEBP", DocumentKind.WebP)]
    public void Gif和WebP只作为二进制文件(string fileName, string signature, DocumentKind expected)
    {
        byte[] content = Encoding.ASCII.GetBytes(signature);

        DocumentClassification result = DocumentClassifier.Classify(fileName, content);

        Assert.AreEqual(expected, result.Kind);
        Assert.IsFalse(result.IsSupportedImage);
    }

    [TestMethod]
    public void 扩展名不能把二进制内容变成文本()
    {
        byte[] content = [0x50, 0x4B, 0x03, 0x04, 0x00];

        DocumentClassification result = DocumentClassifier.Classify("伪装.json", content);

        Assert.AreEqual(DocumentKind.Binary, result.Kind);
    }

    [TestMethod]
    public void 拒绝非法Utf8()
    {
        byte[] content = [0xC3, 0x28];

        DocumentClassification result = DocumentClassifier.Classify("文本.txt", content);

        Assert.AreEqual(DocumentKind.InvalidUtf8, result.Kind);
    }

    [TestMethod]
    public void 移除Utf8Bom且保留正文()
    {
        byte[] content = [0xEF, 0xBB, 0xBF, 0xE4, 0xB8, 0xAD];

        string result = DocumentClassifier.DecodeUtf8(content);

        Assert.AreEqual("中", result);
    }

    [TestMethod]
    public void Sql或未知扩展名的Utf8文本可预览()
    {
        byte[] content = Encoding.UTF8.GetBytes("SELECT * FROM users;\n");

        DocumentClassification sql = DocumentClassifier.Classify("R_2026_M1028_ops.sql", content);
        DocumentClassification unknown = DocumentClassifier.Classify("script.unknown-ext", content);

        Assert.AreEqual(DocumentKind.Text, sql.Kind);
        Assert.AreEqual(DocumentKind.Text, unknown.Kind);
        Assert.AreEqual(DocumentTextEncoding.Utf8, sql.TextEncoding);
        Assert.AreEqual("SELECT * FROM users;\n", DocumentClassifier.DecodeText(content, sql));
    }

    [TestMethod]
    public void Utf16带Bom的Sql按文本预览()
    {
        const string text = "-- 迁移脚本\r\nCREATE TABLE 用户 (id BIGINT);\r\n";
        byte[] payload = Encoding.Unicode.GetBytes(text);
        byte[] content = [0xFF, 0xFE, .. payload];

        DocumentClassification result = DocumentClassifier.Classify("R_2026_M1028_ops.sql", content);

        Assert.AreEqual(DocumentKind.Text, result.Kind);
        Assert.AreEqual(DocumentTextEncoding.Utf16Le, result.TextEncoding);
        Assert.AreEqual("UTF-16 LE", result.EncodingName);
        Assert.AreEqual(text, DocumentClassifier.DecodeText(content, result));
    }

    [TestMethod]
    public void Utf16无Bom的Sql按高置信度文本预览()
    {
        const string text = "CREATE TABLE users (id BIGINT);\r\n";
        byte[] content = Encoding.Unicode.GetBytes(text);

        DocumentClassification result = DocumentClassifier.Classify("migration.sql", content);

        Assert.AreEqual(DocumentKind.Text, result.Kind);
        Assert.AreEqual(DocumentTextEncoding.Utf16Le, result.TextEncoding);
        Assert.AreEqual(text, DocumentClassifier.DecodeText(content, result));
    }

    [TestMethod]
    public void Utf16大端带Bom也能预览()
    {
        const string text = "SELECT 1;";
        byte[] content = [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)];

        DocumentClassification result = DocumentClassifier.Classify("query.unknown", content);

        Assert.AreEqual(DocumentKind.Text, result.Kind);
        Assert.AreEqual(DocumentTextEncoding.Utf16Be, result.TextEncoding);
        Assert.AreEqual(text, DocumentClassifier.DecodeText(content, result));
    }

    [TestMethod]
    public void 含Nul的随机二进制仍不冒充文本()
    {
        byte[] content = [0x00, 0x01, 0x02, 0x00, 0x03, 0x04, 0x00, 0x05];

        DocumentClassification result = DocumentClassifier.Classify("payload.bin", content);

        Assert.AreEqual(DocumentKind.Binary, result.Kind);
    }

    [TestMethod]
    [DataRow("README.md", DocumentKind.Markdown)]
    [DataRow("settings.JSON", DocumentKind.Json)]
    [DataRow("source.cs", DocumentKind.Text)]
    public void 有效文本按有限扩展名分类(string fileName, DocumentKind expected)
    {
        byte[] content = Encoding.UTF8.GetBytes("有效文本");

        DocumentClassification result = DocumentClassifier.Classify(fileName, content);

        Assert.AreEqual(expected, result.Kind);
    }
}
