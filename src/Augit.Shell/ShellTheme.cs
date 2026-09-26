namespace Augit.Shell;

/// <summary>
/// 主题的取值规则。
///
/// 这里的两个函数都是纯函数，不与注册表、窗口或设置文件耦合，便于单独验证。
///
/// 背景：主题有三处取值需要区分——设置里的模式（<c>System</c>／<c>Light</c>／<c>Dark</c>）、
/// 解析后的生效主题、以及下发给网页层的 URL 查询值。生效主题用首字母大写的
/// <c>Dark</c>／<c>Light</c>，而网页层判断的是小写的 <c>theme=dark</c>；两者不统一时，
/// 用户在设置里选“深色”后界面仍然是浅色。
///
/// 这个不一致之所以长期没被发现，是因为审计与视觉稿一律用命令行小写参数
/// （<c>--theme dark</c>），走的是“显式主题原样返回”的分支，从未经过设置里的大写取值。
/// </summary>
internal static class ShellTheme
{
    /// <summary>系统深色检测结果到生效主题的换算。</summary>
    /// <param name="isDark">
    /// 系统是否使用深色应用模式；<c>null</c> 表示检测失败或不受支持。
    /// </param>
    /// <remarks>
    /// 参考实现对检测失败的处理是**不切换**、按非深色取值（`isSystemThemeDark()` 返回可空的
    /// <c>Boolean</c>，调用方遇到 <c>null</c> 直接返回而不改主题）。因此这里对 <c>null</c>
    /// 取 <c>Light</c>，不做“检测不出来就当作深色”的假设。
    /// </remarks>
    internal static string FromSystemDark(bool? isDark)
    {
        return isDark == true ? "Dark" : "Light";
    }

    /// <summary>生效主题到 URL 查询值的换算。</summary>
    /// <remarks>
    /// 网页层的主题契约是小写。审计脚本与视觉稿都用小写，因此这里必须归一化到小写，
    /// 否则设置里的大写 <c>Dark</c> 会让 <c>theme=Dark</c> 无法被识别。
    /// </remarks>
    internal static string QueryValue(string theme)
    {
        return theme.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// 主题对应的**窗口表面色**（下发给 WebView2 的 <c>DefaultBackgroundColor</c>）。
    /// </summary>
    /// <param name="theme">生效主题（<c>Dark</c>／<c>Light</c>，大小写不敏感）。</param>
    /// <remarks>
    /// 取值就是网页层 <c>body</c> 的底色 <c>--augit-chrome</c>（`docs/design-system.md` §6.1／§6.2：
    /// 浅色 <c>#E9EAEE</c>、深色 <c>#2B2D30</c>）。必须在**导航之前**就设好，否则深色下
    /// WebView2 会先用默认白底绘制一帧，出现白闪 —— 参考实现切主题时会重绘全部窗口背景，
    /// 不允许出现与主题不一致的底色（`07-theme-dpi-dialogs.md` §1）。
    /// </remarks>
    internal static (int R, int G, int B) SurfaceColor(string theme)
    {
        return theme.Trim().Equals("Dark", StringComparison.OrdinalIgnoreCase)
            ? (0x2B, 0x2D, 0x30)
            : (0xE9, 0xEA, 0xEE);
    }

    /// <summary>
    /// <c>WM_SETTINGCHANGE</c> 携带的区域名是否表示「应用模式」发生了变化。
    /// </summary>
    /// <remarks>
    /// Windows 在系统浅色/深色应用模式切换时广播 <c>WM_SETTINGCHANGE</c>，并把区域名
    /// <c>ImmersiveColorSet</c> 放在 <c>lParam</c> 指向的宽字符串里。其它设置变化也走同一条消息，
    /// 因此必须按区域名过滤，否则系统语言、无障碍等变化都会触发一次主题重算。
    /// </remarks>
    internal static bool IsImmersiveColorSet(string? area)
    {
        return string.Equals(area, "ImmersiveColorSet", StringComparison.Ordinal);
    }

    /// <summary>
    /// 系统应用模式变化后应生效的主题；不需要变化时返回 <c>null</c>。
    /// </summary>
    /// <param name="configuredMode">设置里的主题模式（<c>System</c>／<c>Light</c>／<c>Dark</c>）。</param>
    /// <param name="systemIsDark">系统当前是否深色；<c>null</c> 表示检测失败。</param>
    /// <param name="currentTheme">当前已生效的主题。</param>
    /// <remarks>
    /// 只有设置为跟随系统时才跟随：用户显式选了浅色或深色就不再被系统变化覆盖。
    /// 主题没有实际变化时返回 <c>null</c>，避免向网页层推送无意义的通知。
    /// </remarks>
    internal static string? NextOnSystemChange(string configuredMode, bool? systemIsDark, string currentTheme)
    {
        if (!configuredMode.Trim().Equals("System", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string next = FromSystemDark(systemIsDark);
        return string.Equals(next, currentTheme, StringComparison.Ordinal) ? null : next;
    }
}
