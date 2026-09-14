namespace Augit.App;

internal sealed partial class NativeGitPanel
{
    private int _commitTextWidth;
    private int _commitPushTextWidth;
    private int _amendTextWidth;
    private int _lastCommitTextWidth;
    private int _commitCountTextWidth;
    private int _commitTitleTextWidth;
    private bool _emptyCommitSubtitleFits = true;

    // 字体或 DPI 改变时才度量；勾选、输入与绘制不重新计算文字布局。
    private void MeasureCommitTypography()
    {
        nint dc = NativeMethods.GetDeviceContext(Handle);
        try
        {
            _commitTextWidth = Math.Max(NativeTheme.Scale(53), MeasureTextWidth(dc, UiText.Commit, NativeTheme.UiFont) + NativeTheme.Scale(26));
            _commitPushTextWidth = Math.Max(NativeTheme.Scale(102), MeasureTextWidth(dc, UiText.CommitAndPush, NativeTheme.UiFont) + NativeTheme.Scale(26));
            _amendTextWidth = Math.Max(NativeTheme.Scale(76), MeasureTextWidth(dc, UiText.AmendLastCommit, NativeTheme.UiFont) + NativeTheme.Scale(25));
            _lastCommitTextWidth = MeasureTextWidth(dc, UiText.LastCommit, NativeTheme.UiFont) + NativeTheme.Scale(22);
            _commitCountTextWidth = Math.Max(NativeTheme.Scale(86), MeasureTextWidth(dc, "99 modified", NativeTheme.UiFont) + NativeTheme.Scale(4));
            _commitTitleTextWidth = Math.Max(NativeTheme.Scale(62), MeasureTextWidth(dc, UiText.CommitPanel, NativeTheme.UiMediumFont) + NativeTheme.Scale(4));
        }
        finally { _ = NativeMethods.ReleaseDeviceContext(Handle, dc); }
    }

    private void LayoutCommitSection(int width, int height)
    {
        int gap = NativeTheme.Scale(7);
        int actionWidth = Math.Max(0, width - NativeTheme.Scale(20));
        int settingsWidth = NativeTheme.Scale(27);
        int actionHeight = NativeTheme.ContentHeight(30, 8);
        bool wrapActions = _commitTextWidth + _commitPushTextWidth + settingsWidth + gap * 2 > actionWidth;
        int actionRowsHeight = actionHeight * (wrapActions ? 2 : 1) + (wrapActions ? gap : 0);
        int optionsHeight = NativeTheme.ContentHeight(22, 4);
        int feedbackHeight = NativeTheme.ContentHeight(20, 4);
        int editMinimum = Math.Max(NativeTheme.Scale(80), feedbackHeight + NativeTheme.UiLineHeight + NativeTheme.Scale(12));
        int optionsBlock = Math.Max(NativeTheme.Scale(39), optionsHeight + NativeTheme.Scale(17));
        int minimumCommit = optionsBlock + editMinimum + NativeTheme.Scale(8 + 5) + actionRowsHeight;
        int commitTop = Math.Min(CalculateCommitTop(height), Math.Max(LeftContentTop, height - minimumCommit));
        int listHeight = Math.Max(0, commitTop - LeftContentTop - NativeTheme.Scale(4));
        Move(_changesList, NativeTheme.Scale(6), LeftContentTop + NativeTheme.Scale(2), Math.Max(0, width - NativeTheme.Scale(12)), listHeight);
        int emptyHeight = NativeTheme.ContentHeight(24, 4);
        _emptyCommitSubtitleFits = listHeight >= emptyHeight * 2 + NativeTheme.Scale(2);
        int emptyBlockHeight = _emptyCommitSubtitleFits ? emptyHeight * 2 + NativeTheme.Scale(2) : emptyHeight;
        int emptyTop = LeftContentTop + Math.Max(0, (listHeight - emptyBlockHeight) / 2);
        Move(_emptyChangesTitle, NativeTheme.Scale(12), emptyTop, Math.Max(0, width - NativeTheme.Scale(24)), emptyHeight);
        Move(_emptyChangesSubtitle, NativeTheme.Scale(12), emptyTop + emptyHeight + NativeTheme.Scale(2), Math.Max(0, width - NativeTheme.Scale(24)), emptyHeight);
        UpdateEmptyChangesState();

        int optionsTop = commitTop + NativeTheme.Scale(5);
        int optionsWidth = Math.Max(0, width - NativeTheme.Scale(16));
        int amendWidth = Math.Min(_amendTextWidth, Math.Max(0, optionsWidth - settingsWidth - gap));
        int lastLeft = NativeTheme.Scale(8) + amendWidth + gap;
        int remaining = Math.Max(0, width - NativeTheme.Scale(8) - lastLeft);
        int countWidth = Math.Min(_commitCountTextWidth, Math.Max(0, remaining - settingsWidth - gap));
        int lastWidth = Math.Max(0, remaining - countWidth - gap);
        Move(_amendButton, NativeTheme.Scale(8), optionsTop, amendWidth, optionsHeight);
        Move(_lastCommitButton, lastLeft, optionsTop, lastWidth, optionsHeight);
        Move(_commitChangeCount, width - NativeTheme.Scale(8) - countWidth, optionsTop, countWidth, optionsHeight);

        int actionTop = Math.Max(LeftContentTop, height - NativeTheme.Scale(5) - actionRowsHeight);
        int editTop = Math.Min(commitTop + optionsBlock, actionTop);
        int editBottom = Math.Max(editTop, actionTop - NativeTheme.Scale(8));
        int labelHeight = Math.Min(feedbackHeight, Math.Max(0, editBottom - editTop - NativeTheme.Scale(4)));
        int messageTop = Math.Min(editBottom, editTop + NativeTheme.Scale(3) + labelHeight);
        int messageHeight = Math.Max(0, editBottom - NativeTheme.Scale(1) - messageTop);
        _commitEditFrame = new() { Left = NativeTheme.Scale(8), Top = editTop, Right = Math.Max(NativeTheme.Scale(8), width - NativeTheme.Scale(8)), Bottom = editBottom };
        Move(_commitLabel, NativeTheme.Scale(14), editTop + NativeTheme.Scale(3), Math.Max(0, width - NativeTheme.Scale(28)), labelHeight);
        Move(_commitEdit, NativeTheme.Scale(9), messageTop, Math.Max(0, width - NativeTheme.Scale(18)), messageHeight);
        ApplyRoundedRegion(_commitEdit, Math.Max(0, width - NativeTheme.Scale(18)), messageHeight, NativeTheme.Scale(5));

        int commitWidth = Math.Min(_commitTextWidth, Math.Max(0, actionWidth - settingsWidth - gap));
        int pushLeft = wrapActions ? NativeTheme.Scale(10) : NativeTheme.Scale(10) + commitWidth + gap;
        int pushTop = wrapActions ? actionTop + actionHeight + gap : actionTop;
        int pushAvailable = wrapActions ? actionWidth : Math.Max(0, actionWidth - commitWidth - settingsWidth - gap * 2);
        Move(_commitButton, NativeTheme.Scale(10), actionTop, commitWidth, actionHeight);
        Move(_commitAndPushButton, pushLeft, pushTop, Math.Min(_commitPushTextWidth, pushAvailable), actionHeight);
        Move(_commitSettingsButton, Math.Max(0, width - NativeTheme.Scale(35)), actionTop + (actionHeight - NativeTheme.Scale(30)) / 2, settingsWidth, NativeTheme.Scale(30));
    }
}
