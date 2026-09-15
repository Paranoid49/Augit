using System.Runtime.InteropServices;

namespace Augit.Shell;

/// <summary>
/// 应用入口：解析启动参数并运行原生窗口消息循环。
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        try
        {
            ShellOptions options = ShellOptions.Parse(arguments);
            using ShellWindow window = new(options);
            window.Show();
            return ShellWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            _ = MessageBox(0, exception.ToString(), "Augit", 0x00000010);
            return 1;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint window, string text, string caption, uint type);
}
