using System.Text.Json;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class SettingsStageTraceTests
{
    [TestMethod]
    [DataRow("settings/read")]
    [DataRow("settings/write")]
    [DataRow("settings/fonts")]
    [DataRow("git/detect")]
    [DataRow("window/settings-ready")]
    [DataRow("window/settings-present")]
    [DataRow("window/settings-close")]
    [DataRow("window/settings-drag")]
    [DataRow("window/settings-failed")]
    [DataRow("window/settings-preview")]
    public void 已知消息诊断保留协议名(string method)
        => Assert.AreEqual(method, ShellWindow.SettingsTraceMethod(method));

    [TestMethod]
    [DataRow("")]
    [DataRow("settings/read/用户输入")]
    [DataRow("window/settings-open")]
    [DataRow("window/settings-ready\n私人路径")]
    public void 未知消息诊断不保留输入内容(string method)
        => Assert.AreEqual("unknown-settings-message", ShellWindow.SettingsTraceMethod(method));

    [TestMethod]
    public void 阶段快照独立且按单调时钟排序并限制容量()
    {
        SettingsStageTrace trace = new();
        trace.Record("created");
        var first = trace.Snapshot();
        for (int i = 0; i < 100; i++) trace.Record("stage");
        var current = trace.Snapshot();
        Assert.HasCount(1, first);
        Assert.HasCount(64, current);
        Assert.IsTrue(current.Zip(current.Skip(1)).All(pair => pair.First.Timestamp <= pair.Second.Timestamp));
        Assert.IsTrue(current.All(sample => sample.Thread == Environment.CurrentManagedThreadId));
    }

    [TestMethod]
    public async Task 导出保留两份不可覆盖的阶段及统一时钟()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Augit-stage-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            SettingsStageTrace trace = new();
            trace.Record("presented");
            await trace.ExportAsync(directory);
            trace.Record("closed");
            await trace.ExportAsync(directory);
            string[] files = Directory.GetFiles(directory);
            Assert.HasCount(2, files);
            foreach (string file in files)
            {
                using JsonDocument data = JsonDocument.Parse(await File.ReadAllTextAsync(file));
                Assert.AreEqual(Environment.ProcessId, data.RootElement.GetProperty("processId").GetInt32());
                Assert.IsGreaterThan(0L, data.RootElement.GetProperty("frequency").GetInt64());
                Assert.IsGreaterThanOrEqualTo(data.RootElement.GetProperty("originTimestamp").GetInt64(), data.RootElement.GetProperty("samples")[0].GetProperty("Timestamp").GetInt64());
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task 未启用及无效导出不抛异常也不创建目录()
    {
        SettingsStageTrace trace = new();
        await trace.ExportAsync(null);
        await trace.ExportAsync("relative");
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await trace.ExportAsync(missing);
        Assert.IsFalse(Directory.Exists(missing));
    }
}
