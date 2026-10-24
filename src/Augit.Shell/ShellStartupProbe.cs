using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace Augit.Shell;

// 启动前的运行时探测：只判断 WebView2 是否存在，不启动浏览器进程，也不改变界面。
// .NET 运行时缺失时应用宿主根本不会进入 Main，由 .NET 自带的对话框说明。
internal static class ShellStartupProbe
{
    private static readonly Lock LoaderGate = new();
    private static nint _loaderModule;

    internal static bool HasWebView2Runtime()
    {
        if (!EnsureLoaderLoaded())
        {
            return false;
        }

        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or BadImageFormatException
            or COMException)
        {
            return false;
        }
    }

    /// <summary>
    /// WebView2 的原生加载器随包放在 <c>native</c> 目录，不在应用根目录；
    /// 必须先显式加载，否则 CoreWebView2Environment 的静态探测会找不到 DLL。
    /// 与 ShellWindow.EnsureLoaderLoaded 使用同一路径；重复加载只增加模块引用计数。
    /// </summary>
    private static bool EnsureLoaderLoaded()
    {
        lock (LoaderGate)
        {
            if (_loaderModule != 0)
            {
                return true;
            }

            string path = Path.Combine(AppContext.BaseDirectory, "native", "WebView2Loader.dll");
            if (!File.Exists(path))
            {
                return false;
            }

            _loaderModule = LoadLibrary(path);
            return _loaderModule != 0;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryW", SetLastError = true)]
    private static extern nint LoadLibrary(string path);
}
