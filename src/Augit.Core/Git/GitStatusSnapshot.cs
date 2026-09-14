namespace Augit.Core.Git;

public enum GitChangeGroup
{
    Changes,
    UnversionedFiles,
}

public enum GitChangeKind
{
    Modified,
    Added,
    Deleted,
    Renamed,
    Copied,
    TypeChanged,
    Unmerged,
    Untracked,
}

public sealed record GitChangedFile(
    string RelativePath,
    string? OriginalRelativePath,
    GitChangeGroup Group,
    GitChangeKind Kind,
    bool HasStagedChanges,
    bool HasWorkingTreeChanges);

public sealed record GitStatusSnapshot(
    string? CurrentBranch,
    bool IsDetached,
    IReadOnlyList<GitChangedFile> Files,
    string? HeadCommit = null)
{
    public IReadOnlyList<GitChangedFile> Changes => Files
        .Where(file => file.Group == GitChangeGroup.Changes)
        .ToArray();

    public IReadOnlyList<GitChangedFile> UnversionedFiles => Files
        .Where(file => file.Group == GitChangeGroup.UnversionedFiles)
        .ToArray();
}

public sealed record GitStatusResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitStatusSnapshot? Snapshot)
{
    public static GitStatusResult Success(GitStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(true, GitOperationFailureKind.None, null, snapshot);
    }

    public static GitStatusResult Failure(GitOperationFailureKind failureKind, string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null);
    }
}

public sealed class GitFileSelection
{
    private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownPaths = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> SelectedPaths => _selectedPaths;

    public bool IsSelected(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return _selectedPaths.Contains(relativePath);
    }

    public void SetSelected(string relativePath, bool selected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (selected)
        {
            _selectedPaths.Add(relativePath);
        }
        else
        {
            _selectedPaths.Remove(relativePath);
        }
    }

    public void SetGroupSelected(IEnumerable<GitChangedFile> files, bool selected)
    {
        ArgumentNullException.ThrowIfNull(files);
        foreach (GitChangedFile file in files)
        {
            SetSelected(file.RelativePath, selected);
        }
    }

    public void Reconcile(IEnumerable<GitChangedFile> currentFiles)
    {
        ArgumentNullException.ThrowIfNull(currentFiles);
        GitChangedFile[] files = currentFiles.ToArray();
        HashSet<string> currentPaths = files
            .Select(file => file.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedPaths.RemoveWhere(path => !currentPaths.Contains(path));
        _knownPaths.RemoveWhere(path => !currentPaths.Contains(path));

        foreach (GitChangedFile file in files)
        {
            if (_knownPaths.Add(file.RelativePath) && file.Group == GitChangeGroup.Changes)
            {
                _selectedPaths.Add(file.RelativePath);
            }
        }
    }
}
