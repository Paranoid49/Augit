using Augit.Shell;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellPrettyDateTests
{
    [TestMethod]
    public void 近期日期采用参考分钟边界及跨年昨天()
    {
        DateTimeOffset now = new(2026, 1, 1, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 1, 1)));
        Assert.AreEqual("Moments ago", ShellPrettyDate.Format(now.AddSeconds(-20), now));
        Assert.AreEqual("Moments ago", ShellPrettyDate.Format(now.AddSeconds(-30), now));
        Assert.AreEqual("A minute ago", ShellPrettyDate.Format(now.AddMinutes(-1), now));
        Assert.AreEqual("2 minutes ago", ShellPrettyDate.Format(now.AddMinutes(-1.5), now));
        Assert.AreEqual("2 minutes ago", ShellPrettyDate.Format(now.AddMinutes(-2.5), now));
        Assert.AreEqual("1 hour ago", ShellPrettyDate.Format(now.AddMinutes(-61), now));
        Assert.AreEqual("Today 10:58", ShellPrettyDate.Format(now.AddMinutes(-62), now));
        Assert.AreEqual("Yesterday 12:00", ShellPrettyDate.Format(now.AddDays(-1), now));
        Assert.AreEqual("2025/12/30 12:00", ShellPrettyDate.Format(now.AddDays(-2), now));
        Assert.AreEqual("Today 12:01", ShellPrettyDate.Format(now.AddMinutes(1), now));
    }
}
