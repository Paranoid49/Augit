using Augit.Core.Files;
using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

public sealed class GitStatusService : IGitStatusService
{
    private readonly GitRuntimeInfo _runtime;
    private readonly GitCommandRunner _runner;

    public GitStatusService(GitRuntimeInfo runtime)
        : this(runtime, new GitCommandRunner(TimeSpan.FromSeconds(30), 16 * 1024 * 1024))
    {
    }

    internal GitStatusService(GitRuntimeInfo runtime, GitCommandRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!runtime.IsAvailable || string.IsNullOrWhiteSpace(runtime.ExecutablePath))
        {
            throw new ArgumentException("Git 运行环境不可用。", nameof(runtime));
        }

        _runtime = runtime;
        _runner = runner;
    }

    public async Task<GitStatusResult> ReadAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (repository.Kind != GitRepositoryKind.WorkingTree || string.IsNullOrWhiteSpace(repository.RepositoryRoot))
        {
            return GitStatusResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Changes 只适用于具有工作区的 Git 仓库。");
        }

        GitCommandResult statusResult = await RunQueryAsync(
            repository.RepositoryRoot,
            [
                "status",
                "--porcelain=v1",
                "-z",
                "--untracked-files=all",
                "--ignore-submodules=all",
                "--renames",
            ],
            cancellationToken).ConfigureAwait(false);
        if (!statusResult.IsSuccess)
        {
            return Failure(statusResult);
        }

        if (statusResult.IsOutputTruncated)
        {
            return GitStatusResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "Git 状态输出超过 16 MB，Changes 已停止刷新。");
        }

        if (!TryParseStatus(statusResult.StandardOutput, out IReadOnlyList<GitChangedFile>? files))
        {
            return GitStatusResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "无法解析 Git 状态，Changes 已停止刷新。");
        }

        GitCommandResult branchResult = await RunQueryAsync(
            repository.RepositoryRoot,
            ["branch", "--show-current"],
            cancellationToken).ConfigureAwait(false);
        if (!branchResult.IsSuccess)
        {
            return Failure(branchResult);
        }

        string? branch = NullIfEmpty(branchResult.StandardOutput);
        bool isDetached = false;
        GitCommandResult headResult = await RunQueryAsync(
            repository.RepositoryRoot,
            ["rev-parse", "--verify", "HEAD"],
            cancellationToken).ConfigureAwait(false);
        string? headCommit = null;
        if (headResult.IsSuccess)
        {
            headCommit = NullIfEmpty(headResult.StandardOutput);
            if (branch is null && headCommit is not null)
            {
                branch = headCommit[..Math.Min(12, headCommit.Length)];
                isDetached = true;
            }
        }
        else if (!IsUnbornHead(headResult.ErrorMessage))
        {
            return Failure(headResult);
        }

        GitStatusSnapshot snapshot = new(branch, isDetached, files!, headCommit);
        return GitStatusResult.Success(snapshot);
    }

    internal static bool TryParseStatus(string output, out IReadOnlyList<GitChangedFile>? files)
    {
        files = null;
        string[] records = output.Split('\0');
        List<GitChangedFile> parsed = [];
        for (int index = 0; index < records.Length; index++)
        {
            string record = records[index];
            if (record.Length == 0)
            {
                continue;
            }

            if (record.Length < 4 || record[2] != ' ')
            {
                return false;
            }

            char indexState = record[0];
            char workTreeState = record[1];
            string relativePath = NormalizeRelativePath(record[3..]);
            string? originalPath = null;
            if (indexState is 'R' or 'C' || workTreeState is 'R' or 'C')
            {
                if (++index >= records.Length || records[index].Length == 0)
                {
                    return false;
                }

                originalPath = NormalizeRelativePath(records[index]);
            }

            GitChangeGroup group = indexState == '?' && workTreeState == '?'
                ? GitChangeGroup.UnversionedFiles
                : GitChangeGroup.Changes;
            parsed.Add(new(
                relativePath,
                originalPath,
                group,
                GetChangeKind(indexState, workTreeState),
                group == GitChangeGroup.Changes && indexState != ' ',
                group == GitChangeGroup.UnversionedFiles || workTreeState != ' ',
                indexState,
                workTreeState));
        }

        files = MergeSamePathStatuses(parsed)
            .OrderBy(file => file.Group)
            .ThenBy(file => Path.GetFileName(file.RelativePath), NaturalNameComparer.Instance)
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return true;
    }

    private static Dictionary<string, GitChangedFile>.ValueCollection MergeSamePathStatuses(
        IEnumerable<GitChangedFile> files)
    {
        Dictionary<string, GitChangedFile> merged = new(StringComparer.OrdinalIgnoreCase);
        foreach (GitChangedFile file in files)
        {
            if (!merged.TryGetValue(file.RelativePath, out GitChangedFile? existing))
            {
                merged.Add(file.RelativePath, file);
                continue;
            }

            // `git rm --cached path` 后磁盘文件仍存在时，porcelain 会为同一路径输出
            // `D  path` 与 `?? path`。产品列表仍只显示一行，但必须保留索引和工作区两侧状态。
            GitChangedFile primary = existing.Group == GitChangeGroup.Changes ? existing : file;
            GitChangedFile secondary = ReferenceEquals(primary, existing) ? file : existing;
            merged[file.RelativePath] = primary with
            {
                Group = GitChangeGroup.Changes,
                OriginalRelativePath = primary.OriginalRelativePath ?? secondary.OriginalRelativePath,
                HasStagedChanges = existing.HasStagedChanges || file.HasStagedChanges,
                HasWorkingTreeChanges = existing.HasWorkingTreeChanges || file.HasWorkingTreeChanges,
                IndexStatus = MergeStatusColumn(primary.IndexStatus, secondary.IndexStatus),
                WorkTreeStatus = MergeStatusColumn(primary.WorkTreeStatus, secondary.WorkTreeStatus),
            };
        }

        return merged.Values;
    }

    private static char MergeStatusColumn(char primary, char secondary)
    {
        if (primary is not (' ' or '?'))
        {
            return primary;
        }

        return secondary != ' ' ? secondary : primary;
    }

    private Task<GitCommandResult> RunQueryAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        return _runner.RunAsync(
            _runtime.ExecutablePath!,
            workingDirectory,
            arguments,
            GitCommandMode.LocalQuery,
            cancellationToken);
    }

    private static GitStatusResult Failure(GitCommandResult result)
    {
        return GitStatusResult.Failure(
            result.FailureKind == GitOperationFailureKind.None
                ? GitOperationFailureKind.CommandFailed
                : result.FailureKind,
            result.ErrorMessage);
    }

    private static GitChangeKind GetChangeKind(char indexState, char workTreeState)
    {
        if (indexState == '?' && workTreeState == '?')
        {
            return GitChangeKind.Untracked;
        }

        if (IsUnmerged(indexState, workTreeState))
        {
            return GitChangeKind.Unmerged;
        }

        if (indexState == 'R' || workTreeState == 'R')
        {
            return GitChangeKind.Renamed;
        }

        if (indexState == 'C' || workTreeState == 'C')
        {
            return GitChangeKind.Copied;
        }

        if (indexState == 'D' || workTreeState == 'D')
        {
            return GitChangeKind.Deleted;
        }

        if (indexState == 'A' || workTreeState == 'A')
        {
            return GitChangeKind.Added;
        }

        if (indexState == 'T' || workTreeState == 'T')
        {
            return GitChangeKind.TypeChanged;
        }

        return GitChangeKind.Modified;
    }

    private static bool IsUnmerged(char indexState, char workTreeState)
    {
        return (indexState, workTreeState) is ('D', 'D')
            or ('A', 'U')
            or ('U', 'D')
            or ('U', 'A')
            or ('D', 'U')
            or ('A', 'A')
            or ('U', 'U');
    }

    private static bool IsUnbornHead(string errorMessage)
    {
        return errorMessage.Contains("Needed a single revision", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("unknown revision", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("bad revision", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string? NullIfEmpty(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
