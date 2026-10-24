using System.Diagnostics;
using System.Text.Json;

namespace Augit.Shell;

// 外部刷新链路的宿主阶段跟踪：监听接收、合并、桥接通知、文档/Git 读取、网页应用与帧回调。
// 只有显式设置 AUGIT_REFRESH_TRACE_DIRECTORY 时才记录；关闭时所有入口都是空操作，
// 不参与窗口等待、显示判定或产品状态。记录只保存阶段名与时间戳，不保存文档内容。
internal sealed class RefreshStageTrace
{
    internal sealed record Sample(string Stage, string? Detail, long Timestamp, long UtcTicks, int Thread);

    private const int Capacity = 256;
    private readonly Lock _gate = new();
    private readonly List<Sample> _samples = new(Capacity);
    private readonly long _origin = Stopwatch.GetTimestamp();
    private readonly DateTimeOffset _utc = DateTimeOffset.UtcNow;
    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly string? _directory;

    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal RefreshStageTrace(string? directory)
    {
        // 只接受已存在的绝对目录；诊断路径无效时静默停用，不能因此改变产品流程。
        _directory = !string.IsNullOrWhiteSpace(directory)
            && Path.IsPathFullyQualified(directory)
            && Directory.Exists(directory)
            ? Path.GetFullPath(directory)
            : null;
    }

    internal bool IsEnabled => _directory is not null;

    internal static RefreshStageTrace FromEnvironment() =>
        new(Environment.GetEnvironmentVariable("AUGIT_REFRESH_TRACE_DIRECTORY"));

    internal void Record(string stage, string? detail = null)
    {
        if (_directory is null)
        {
            return;
        }

        lock (_gate)
        {
            // 诊断本身不能无界增长：首批样本已经覆盖一次刷新的完整阶段。
            if (_samples.Count >= Capacity)
            {
                return;
            }

            _samples.Add(new(
                stage,
                detail,
                Stopwatch.GetTimestamp(),
                DateTimeOffset.UtcNow.UtcTicks,
                Environment.CurrentManagedThreadId));
        }
    }

    /// <summary>
    /// 窗口销毁阶段同步导出。只在启用时写一个小 JSON 文件；失败不影响关闭流程。
    /// </summary>
    internal void Export()
    {
        if (_directory is null)
        {
            return;
        }

        Sample[] samples;
        lock (_gate)
        {
            samples = [.. _samples];
        }

        string file = Path.Combine(_directory, $"refresh-{Environment.ProcessId}-{_id}.json");
        try
        {
            string json = JsonSerializer.Serialize(new
            {
                processId = Environment.ProcessId,
                originTimestamp = _origin,
                utc = _utc,
                frequency = Stopwatch.Frequency,
                samples,
                limitation = "宿主阶段与网页回调不等于桌面最终呈现；可见首内容仍以同轮屏幕采集帧为准。",
            }, ExportOptions);
            File.WriteAllText(file, json, new System.Text.UTF8Encoding(false));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            // 导出失败不能中断窗口关闭；采集器负责报告缺失证据。
        }
    }
}
