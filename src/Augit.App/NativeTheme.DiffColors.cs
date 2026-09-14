namespace Augit.App;

internal static partial class NativeTheme
{
    // 与视觉稿的 green-soft / red-soft 共用色值；整行底色与字词高亮分别绘制。
    internal static (int Red, int Green, int Blue) DiffLineBackground(bool added, bool dark) =>
        (added, dark) switch
        {
            (true, false) => (201, 238, 207),
            (false, false) => (247, 215, 215),
            (true, true) => (41, 67, 48),
            _ => (75, 45, 45),
        };

    internal static void ApplyDiffLineColors(
        ScintillaControl? unified, ScintillaControl? oldSide, ScintillaControl? newSide, bool dark)
    {
        var removed = DiffLineBackground(added: false, dark);
        var added = DiffLineBackground(added: true, dark);
        unified?.SetLineBackgroundColor(0, removed.Red, removed.Green, removed.Blue);
        unified?.SetLineBackgroundColor(1, added.Red, added.Green, added.Blue);
        oldSide?.SetLineBackgroundColor(0, removed.Red, removed.Green, removed.Blue);
        newSide?.SetLineBackgroundColor(0, added.Red, added.Green, added.Blue);
    }
}
