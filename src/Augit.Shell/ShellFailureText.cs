namespace Augit.Shell;

/// <summary>宿主无法呈现网页时使用固定英文说明，不直接显示本地化异常或堆栈。</summary>
internal static class ShellFailureText
{
    public static string Startup(Exception exception, bool interfaceOnly = false)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string action = interfaceOnly ? "The Augit interface could not be loaded." : "Augit could not start.";
        return $"{action}\nCheck the .NET 10 and WebView2 runtimes and the application files.\n"
            + $"Error: {exception.GetType().Name} (0x{exception.HResult:X8}).";
    }

    public static string Navigation(string status, string resourceRoot) =>
        $"The interface resources could not be loaded: {status}.\nResource directory: {resourceRoot}";

    public static string Renderer(string kind, string reason) =>
        $"The interface rendering process stopped: {kind} ({reason}).\nClose and reopen this window.";
}
