namespace Augit.Shell;

/// <summary>
/// 读取 Windows 的「应用模式」设置（个性化 → 颜色）。
///
/// 注册表路径与取值与参考实现一致：<c>Software\Microsoft\Windows\CurrentVersion\Themes\Personalize</c>
/// 下的 <c>AppsUseLightTheme</c>（<c>0</c> 表示深色）。与 <see cref="ShellTheme"/> 分开，
/// 是为了让取值规则保持纯函数、可以被单独验证；这里是唯一读注册表的地方。
/// </summary>
internal static class ShellSystemTheme
{
    /// <summary>
    /// 系统是否使用深色应用模式。
    /// </summary>
    /// <returns>
    /// <c>true</c>／<c>false</c> 表示检测到的结果；<c>null</c> 表示检测失败或取值不可用
    /// （注册表不可读，或 <c>AppsUseLightTheme</c> 不是整数）。调用方按非深色处理，
    /// 不做「检测不出来就当作深色」的假设。
    /// </returns>
    internal static bool? DetectDark()
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value ? value == 0 : null;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return null;
        }
    }
}
