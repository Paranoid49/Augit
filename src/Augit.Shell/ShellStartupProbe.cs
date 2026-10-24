using Microsoft.Web.WebView2.Core;

namespace Augit.Shell;

// 启动前的运行时探测：只判断 WebView2 是否存在，不启动浏览器进程，也不改变界面。
// .NET 运行时缺失时应用宿主根本不会进入 Main，由 .NET 自带的对话框说明。
internal static class ShellStartupProbe
{
    internal static bool HasWebView2Runtime()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException)
        {
            // Loader 缺失或架构不匹配与运行时缺失一样无法显示界面，交由同一提示处理。
            return false;
        }
    }
}
