using System;
using Augit.Infrastructure.Interop;

namespace Augit.Infrastructure.Tests;

/// <summary>
/// 剪贴板读写测试。
/// </summary>
/// <remarks>
/// 这组测试针对的是**真实的 Win32 调用**，不是替身：写入后读回，验证整对调用正确，
/// 包括 CF_UNICODETEXT 的编码、结尾 NUL、以及 SetClipboardData 之后不再释放句柄
/// （所有权已转移给系统；误释放会让剪贴板内容变成悬空引用）。
///
/// 会占用系统剪贴板，因此测试前保存、测试后恢复原内容。
///
/// **必须禁用并行**：剪贴板是全局独占资源，`OpenClipboard` 在别的测试持有时会失败。
/// 项目全局开启了方法级并行（`MSTestSettings`），实测并行时 4 条里有 2 条因争用失败。
///
/// **验证强度（如实说明）**：这组测试能证明 P/Invoke 的签名与调用约定可用、
/// Unicode（含 CJK 与 emoji）往返一致、长文本的字节数计算正确、空输入如实失败。
/// 但它**无法**区分两个细节：
/// <list type="bullet">
/// <item>漏写结尾 NUL —— `GlobalAlloc` 的新页面通常是零，NUL 恰好存在；
/// 尝试用"先写长文本再写短文本看残留"的构造也未能暴露（`PtrToStringUni` 遇 NUL 即停）。</item>
/// <item>`SetClipboardData` 后误释放句柄 —— 实测读回仍然正确，本环境无法区分。</item>
/// </list>
/// 因此不要把这两条记作已覆盖。
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ClipboardTextTests
{
    private string? _original;
    private bool _hadOriginal;

    /// <summary>
    /// 剪贴板是全局独占资源，其他进程（甚至系统组件）可能瞬时占用。
    /// 因此写入带一个有界重试；重试仍失败才算真失败——不把外部争用误报成实现缺陷。
    /// </summary>
    private static ClipboardWriteResult WriteWithRetry(string text, int attempts = 5)
    {
        ClipboardWriteResult result = ClipboardWriteResult.Failure("尚未尝试。");
        for (int index = 0; index < attempts; index++)
        {
            result = ClipboardText.TrySetText(text);
            if (result.IsSuccess)
            {
                return result;
            }

            Thread.Sleep(30);
        }

        return result;
    }

    [TestInitialize]
    public void Initialize()
    {
        _original = ClipboardText.TryGetText();
        _hadOriginal = _original is not null;
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_hadOriginal && _original is not null)
        {
            ClipboardText.TrySetText(_original);
        }
    }

    [TestMethod]
    public void 写入后在剪贴板读回同样的文本()
    {
        string text = "Augit 剪贴板验证 " + Guid.NewGuid().ToString("N");

        ClipboardWriteResult result = WriteWithRetry(text);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(text, ClipboardText.TryGetText());
    }

    // 中文与 emoji 都走 UTF-16，但仍单独核对一次：编码写错时会出现半个字符。
    [TestMethod]
    public void 非ASCII文本往返一致()
    {
        string text = "中文与🙂混排 " + Guid.NewGuid().ToString("N");

        Assert.IsTrue(WriteWithRetry(text).IsSuccess);
        Assert.AreEqual(text, ClipboardText.TryGetText());
    }

    // 空文本必须如实失败：写入空串会让剪贴板变成"有格式但内容为空"。
    [TestMethod]
    public void 空文本如实失败()
    {
        ClipboardWriteResult result = ClipboardText.TrySetText(string.Empty);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    // 长文本验证 GlobalAlloc 的字节数计算（含结尾 NUL）。
    [TestMethod]
    public void 长文本往返一致()
    {
        string text = new string('字', 5000) + Guid.NewGuid().ToString("N");

        Assert.IsTrue(WriteWithRetry(text).IsSuccess);
        Assert.AreEqual(text, ClipboardText.TryGetText());
    }
}
