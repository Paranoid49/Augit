using Augit.Core.Files;

namespace Augit.Infrastructure.Git;

internal static class GitPathValidator
{
    internal static bool TryNormalizeRelativePath(
        string repositoryRoot,
        string relativePath,
        out string? normalizedPath,
        out string? fullPath)
    {
        normalizedPath = null;
        fullPath = null;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathFullyQualified(relativePath))
        {
            return false;
        }

        try
        {
            string candidate = Path.GetFullPath(Path.Combine(
                repositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!WorkspacePathRules.IsWithin(repositoryRoot, candidate))
            {
                return false;
            }

            normalizedPath = Path.GetRelativePath(repositoryRoot, candidate).Replace('\\', '/');
            fullPath = candidate;
            return normalizedPath.Length > 0 && normalizedPath != ".";
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
