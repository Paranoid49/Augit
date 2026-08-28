namespace Augit.Core.Files;

public static class WorkspacePathRules
{
    public static bool IsWithin(string rootPath, string candidatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);

        string root = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
        string candidate = Path.GetFullPath(candidatePath);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || candidate.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsHiddenGitEntry(string path)
    {
        return Path.GetFileName(path).Equals(".git", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> NormalizeExpandedDirectories(string rootPath, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => IsWithin(rootPath, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
    }
}
