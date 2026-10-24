using System.Text.Json;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class RefreshStageTraceTests
{
    [TestMethod]
    public void 未配置目录时不启用且记录是空操作()
    {
        RefreshStageTrace trace = new(null);

        Assert.IsFalse(trace.IsEnabled);
        trace.Record("watch-received");
        // 未配置目录时导出必须是静默空操作，不抛异常、不写文件。
        trace.Export();
    }

    [TestMethod]
    public void 目录不存在时停用()
    {
        string missing = Path.Combine(Path.GetTempPath(), "augit-refresh-trace-missing-" + Guid.NewGuid().ToString("N"));

        RefreshStageTrace trace = new(missing);

        Assert.IsFalse(trace.IsEnabled);
    }

    [TestMethod]
    public void 启用后导出阶段与时钟锚点()
    {
        using TemporaryDirectory directory = new();
        RefreshStageTrace trace = new(directory.FullPath);

        Assert.IsTrue(trace.IsEnabled);
        trace.Record("watch-received");
        trace.Record("page-applied", "a.txt");
        trace.Export();

        string file = Directory.GetFiles(directory.FullPath, "refresh-*.json").Single();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
        JsonElement root = document.RootElement;
        JsonElement samples = root.GetProperty("samples");

        Assert.AreEqual(2, samples.GetArrayLength());
        Assert.AreEqual("watch-received", samples[0].GetProperty("stage").GetString());
        Assert.AreEqual("page-applied", samples[1].GetProperty("stage").GetString());
        Assert.AreEqual("a.txt", samples[1].GetProperty("detail").GetString());
        Assert.IsGreaterThan(0L, root.GetProperty("frequency").GetInt64());
        Assert.IsGreaterThan(0L, samples[1].GetProperty("utcTicks").GetInt64());
        Assert.IsTrue(root.GetProperty("limitation").GetString()!.Contains("屏幕采集", StringComparison.Ordinal));
    }
}
