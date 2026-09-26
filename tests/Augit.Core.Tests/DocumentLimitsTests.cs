using Augit.Core.Documents;

namespace Augit.Core.Tests;

/// <summary>
/// 文档尺寸限制的三档默认值。权威 `FileSizeLimit`（`platform/core-api/src/com/intellij/openapi/vfs/limits/
/// FileSizeLimit.kt:14-24,60-110`）与 `FileUtilRt`（`platform/util-rt/src/com/intellij/openapi/util/io/
/// FileUtilRt.java:1089-1101`）：内容加载 20 MB、智能感知 2500 KB、预览 2500 KB；
/// 扩展名登记只能**放大**，本 checkout 里没有任何扩展名实现 ⇒ 一律取默认值。
/// </summary>
[TestClass]
public sealed class DocumentLimitsTests
{
    [TestMethod]
    public void 没有扩展名或未登记的扩展名都用默认值()
    {
        foreach (string path in new[] { "notes", "notes.txt", "notes.LOG", "", "  " })
        {
            Assert.AreEqual(DocumentLimits.DefaultContentLoadBytes, DocumentLimits.ContentLoadLimit(path), path);
            Assert.AreEqual(DocumentLimits.DefaultIntellisenseBytes, DocumentLimits.IntellisenseLimit(path), path);
            Assert.AreEqual(DocumentLimits.DefaultPreviewBytes, DocumentLimits.PreviewLimit(path), path);
        }
    }

    [TestMethod]
    public void 预览上限不超过内容加载上限()
    {
        // 权威 `LARGE_FILE_PREVIEW_SIZE = min(preview, LARGE_FOR_CONTENT_LOADING)`：预览必须落在内容加载之内，
        // 否则"整页拒绝"和"只读预览"会互相矛盾。
        Assert.IsLessThanOrEqualTo(DocumentLimits.ContentLoadLimit("notes.txt"), DocumentLimits.PreviewLimit("notes.txt"));
        Assert.IsLessThanOrEqualTo(DocumentLimits.ContentLoadLimit("notes.txt"), DocumentLimits.IntellisenseLimit("notes.txt"));
    }
}
