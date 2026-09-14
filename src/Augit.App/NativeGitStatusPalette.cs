using Augit.Core.Git;

namespace Augit.App;

internal static class NativeGitStatusPalette
{
    internal static uint Resolve(GitChangeKind kind, bool dark)
    {
        return kind switch
        {
            GitChangeKind.Added => dark ? Rgb(91, 166, 103) : Rgb(57, 134, 89),
            GitChangeKind.Deleted => dark ? Rgb(231, 117, 117) : Rgb(207, 68, 68),
            GitChangeKind.Renamed or GitChangeKind.Copied => dark ? Rgb(75, 180, 178) : Rgb(32, 139, 142),
            GitChangeKind.TypeChanged => dark ? Rgb(220, 166, 75) : Rgb(167, 107, 31),
            GitChangeKind.Unmerged => dark ? Rgb(231, 117, 117) : Rgb(194, 72, 80),
            GitChangeKind.Untracked => dark ? Rgb(84, 138, 247) : Rgb(56, 113, 225),
            _ => dark ? Rgb(84, 138, 247) : Rgb(56, 113, 225),
        };
    }

    private static uint Rgb(byte red, byte green, byte blue)
    {
        return (uint)(red | green << 8 | blue << 16);
    }
}
