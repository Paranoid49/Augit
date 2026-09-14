using Augit.Core.Git;

namespace Augit.App;

internal sealed class NativeGitTreeStatusIndex
{
    private readonly string _workspaceRoot;
    private readonly Dictionary<string, GitChangeKind> _statuses;

    internal NativeGitTreeStatusIndex(string workspaceRoot, IReadOnlyList<GitChangedFile> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(files);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _statuses = files.ToDictionary(
            file => NormalizeRelativePath(file.RelativePath),
            file => file.Kind,
            StringComparer.OrdinalIgnoreCase);
    }

    internal GitChangeKind? Resolve(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        string relativePath = Path.GetRelativePath(_workspaceRoot, Path.GetFullPath(fullPath));
        if (relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return null;
        }

        return _statuses.TryGetValue(NormalizeRelativePath(relativePath), out GitChangeKind status)
            ? status
            : null;
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }
}
