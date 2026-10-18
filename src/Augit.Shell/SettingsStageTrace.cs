using System.Diagnostics;
using System.Text.Json;

namespace Augit.Shell;

// 只记录原生阶段，不读取文档、配置或输入内容；导出不参与窗口等待及显示判定。
internal sealed class SettingsStageTrace
{
    internal sealed record Sample(string Stage, long Timestamp, int Thread);
    private readonly List<Sample> _samples = new(32);
    private readonly long _origin = Stopwatch.GetTimestamp();
    private readonly DateTimeOffset _utc = DateTimeOffset.UtcNow;
    private readonly string _id = Guid.NewGuid().ToString("N");
    private int _export;

    internal void Record(string stage)
    {
        if (_samples.Count < 64) _samples.Add(new(stage, Stopwatch.GetTimestamp(), Environment.CurrentManagedThreadId));
    }

    internal Sample[] Snapshot() => _samples.ToArray();

    internal Task ExportAsync(string? directory)
    {
        // 仅接受已存在的绝对目录；不能因诊断路径失败而改变产品流程。
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)) return Task.CompletedTask;
        Sample[] samples = Snapshot();
        string file = Path.Combine(directory, $"settings-{Environment.ProcessId}-{_id}-{++_export}.json");
        return Task.Run(async () =>
        {
            try
            {
                await using FileStream stream = new(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                await JsonSerializer.SerializeAsync(stream, new
                {
                    processId = Environment.ProcessId,
                    originTimestamp = _origin,
                    utc = _utc,
                    frequency = Stopwatch.Frequency,
                    samples,
                    limitation = "墙钟锚点有调用间误差；原生阶段不等于桌面最终呈现，也不证明实际字形。"
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // 导出失败不能中断窗口显示或关闭；采集器负责报告缺失证据。
            }
        });
    }
}
