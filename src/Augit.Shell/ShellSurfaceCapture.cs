using System.Text.Json;

namespace Augit.Shell;

/// <summary>直接取得当前页面合成表面的PNG，使用快速无损编码，不超出当前视口。</summary>
internal static class ShellSurfaceCapture
{
    internal static async Task<byte[]> CaptureAsync(Func<string, string, Task<string>> invoke)
    {
        string result = await invoke("Page.captureScreenshot",
            """{"format":"png","fromSurface":true,"captureBeyondViewport":false,"optimizeForSpeed":true}""");
        using JsonDocument response = JsonDocument.Parse(result);
        if (response.RootElement.ValueKind != JsonValueKind.Object
            || !response.RootElement.TryGetProperty("data", out JsonElement data)
            || data.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(data.GetString()))
            throw new InvalidOperationException("背景捕获未返回PNG数据。");
        byte[] png = Convert.FromBase64String(data.GetString()!);
        // 复用首图尺寸验证；失败保留旧背景，由调用方finally恢复表单和遮盖。
        _ = ShellSnapshotPresentation.InitialImageSize(png, 1);
        return png;
    }
}
