namespace Augit.Shell;

/// <summary>宿主无法呈现网页时使用固定英文说明，不直接显示本地化异常或堆栈。</summary>
internal static class ShellFailureText
{
    private const string RuntimeDownloads =
        "Download the official runtimes if they are missing:\n"
        + "  .NET 10 Runtime: https://dotnet.microsoft.com/download/dotnet/10.0\n"
        + "  WebView2 Runtime: https://developer.microsoft.com/microsoft-edge/webview2/";

    public static string Startup(Exception exception, bool interfaceOnly = false)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string action = interfaceOnly ? "The Augit interface could not be loaded." : "Augit could not start.";
        return $"{action}\nCheck the .NET 10 and WebView2 runtimes and the application files.\n"
            + RuntimeDownloads + "\n"
            + $"Error: {exception.GetType().Name} (0x{exception.HResult:X8}).";
    }

    /// <summary>
    /// 便携包在缺少 WebView2 时使用：明确缺失项并给出微软官方下载入口，不尝试自动安装。
    /// </summary>
    public static string MissingWebView2() =>
        "Augit needs the Microsoft Edge WebView2 Runtime to show its interface.\n"
        + "Install the Evergreen WebView2 Runtime from Microsoft:\n"
        + "https://developer.microsoft.com/microsoft-edge/webview2/\n"
        + "Portable builds never install runtimes automatically. Start Augit again after installing it.";

    public static string Navigation(string status, string resourceRoot) =>
        $"The interface resources could not be loaded: {status}.\nResource directory: {resourceRoot}";

    public static string Renderer(string kind, string reason) =>
        $"The interface rendering process stopped: {kind} ({reason}).\nClose and reopen this window.";
}
