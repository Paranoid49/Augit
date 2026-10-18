using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellSettingsEraseTests
{
    [TestMethod]
    public void 设置子窗擦除背景使用主题表面色而不是默认白底()
    {
        string shell = File.ReadAllText(FindRepositoryFile("src/Augit.Shell/ShellWindow.cs"));
        string settings = File.ReadAllText(FindRepositoryFile("src/Augit.Shell/ShellWindow.Settings.cs"));

        StringAssert.Contains(shell, "case 0x0014: // WM_ERASEBKGND");
        StringAssert.Contains(shell, "shell.PaintSettingsBackground(wParam);");
        StringAssert.Contains(shell, "return DefWindowProc(window, message, wParam, lParam);");
        StringAssert.Contains(settings, "private void PaintSettingsBackground(nint hdc)");
        StringAssert.Contains(settings, "_controller.DefaultBackgroundColor");
        StringAssert.Contains(settings, "CreateSolidBrush(colorRef)");
        StringAssert.Contains(settings, "FillRect(hdc, ref bounds, brush)");
        StringAssert.Contains(settings, "DeleteObject(brush)");
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"找不到仓库文件：{relativePath}");
    }
}
