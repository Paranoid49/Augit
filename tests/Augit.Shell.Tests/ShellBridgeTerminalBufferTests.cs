using System.Text;

namespace Augit.Shell.Tests;

/// <summary>
/// 终端输出缓冲区的裁剪（规格 §7.16）。
///
/// 这里锁住的是一条曾经写错、而且症状很隐蔽的算术：缓冲区超过上限时丢掉最旧的一段，
/// 读取偏移量必须**同步减去被丢掉的字符数**。此前的写法是
/// <c>offset - (buffer.Length - max)</c>，但那一项在 <c>Remove</c> 之后已经变成 0，
/// 于是偏移量永远大于缓冲区长度；<c>ReadTerminal</c> 的 Clamp 会把起点夹到末尾，
/// 终端从那一刻起**再也读不到任何输出**（用户在长时间运行后只会看到终端"不动了"）。
/// </summary>
[TestClass]
public sealed class ShellBridgeTerminalBufferTests
{
    [TestMethod]
    public void 未超过上限时不裁剪且偏移不变()
    {
        StringBuilder buffer = new("abcdef");
        long offset = 4;

        ShellBridge.TrimTerminalBuffer(buffer, ref offset, 16);

        Assert.AreEqual("abcdef", buffer.ToString());
        Assert.AreEqual(4, offset);
    }

    [TestMethod]
    public void 超过上限时丢掉最旧字符并同步减少偏移()
    {
        StringBuilder buffer = new("0123456789");
        long offset = 10;

        ShellBridge.TrimTerminalBuffer(buffer, ref offset, 4);

        Assert.AreEqual("6789", buffer.ToString());
        Assert.AreEqual(4, offset, "偏移量必须减去被裁掉的 6 个字符，否则读取起点会被夹到末尾。");
    }

    [TestMethod]
    public void 偏移已在被裁区间内时归零()
    {
        StringBuilder buffer = new("0123456789");
        long offset = 2;

        ShellBridge.TrimTerminalBuffer(buffer, ref offset, 4);

        Assert.AreEqual("6789", buffer.ToString());
        Assert.AreEqual(0, offset);
    }

    /// <summary>回归：裁剪之后继续追加，读取仍能返回新区间（此前偏移错位会永远读到空串）。</summary>
    [TestMethod]
    public void 裁剪后继续追加仍能读到新输出()
    {
        StringBuilder buffer = new("0123456789");
        long offset = 10;
        ShellBridge.TrimTerminalBuffer(buffer, ref offset, 4);

        buffer.Append("AB");
        int start = (int)Math.Clamp(offset, 0, buffer.Length);
        string chunk = buffer.ToString(start, buffer.Length - start);

        Assert.AreEqual("AB", chunk);
    }

    /// <summary>
    /// 裁剪必须摊销：只有超过"上限 + 余量"才裁。
    ///
    /// 逐次裁剪（每 4 KB 追加都 `Remove(0, 超出量)` 整个 4 MB 缓冲区）是二次成本，
    /// 真机实测会让读取线程长期卡住、pty 管道写满、Shell 阻塞 → 终端永久冻结
    ///（§3.2 第 15 条；改成摊销后同一探针 6 秒内就拿到后续输出）。
    /// </summary>
    [TestMethod]
    public void 裁剪阈值留出摊销余量()
    {
        const int maximum = 4 * 1024 * 1024;
        const int slack = 512 * 1024;

        Assert.IsFalse(ShellBridge.ShouldTrimTerminalBuffer(maximum, maximum, slack));
        Assert.IsFalse(ShellBridge.ShouldTrimTerminalBuffer(maximum + slack, maximum, slack));
        Assert.IsTrue(ShellBridge.ShouldTrimTerminalBuffer(maximum + slack + 1, maximum, slack));
    }
}
