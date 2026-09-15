using System.Globalization;
using System.Text.RegularExpressions;
using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

public sealed class GitHistoryService : IGitHistoryService
{
    private const char RecordSeparator = '\x1e';
    private const char FieldSeparator = '\x1f';
    private const int MaximumHistoryOutputBytes = 8 * 1024 * 1024;
    private const int MaximumDetailsOutputBytes = 20 * 1024 * 1024;
    private const long MaximumSideBytes = 10L * 1024 * 1024;
    private const string FullFileContextArgument = "--unified=10485760";
    private readonly GitRuntimeInfo _runtime;
    private readonly GitCommandRunner _queryRunner;
    private readonly GitCommandRunner _detailsRunner;

    public GitHistoryService(GitRuntimeInfo runtime)
        : this(
            runtime,
            new GitCommandRunner(TimeSpan.FromSeconds(30), MaximumHistoryOutputBytes),
            new GitCommandRunner(TimeSpan.FromSeconds(30), MaximumDetailsOutputBytes))
    {
    }

    internal GitHistoryService(
        GitRuntimeInfo runtime,
        GitCommandRunner queryRunner,
        GitCommandRunner detailsRunner)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!runtime.IsAvailable || string.IsNullOrWhiteSpace(runtime.ExecutablePath))
        {
            throw new ArgumentException("Git 运行环境不可用。", nameof(runtime));
        }

        _runtime = runtime;
        _queryRunner = queryRunner;
        _detailsRunner = detailsRunner;
    }

    public async Task<GitHistoryResult> ReadPageAsync(
        GitRepositorySnapshot repository,
        GitHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? validationError))
        {
            return GitHistoryResult.Failure(GitOperationFailureKind.InvalidRequest, validationError!);
        }

        if (request.Page < 0 || request.PageSize is < 1 or > 500)
        {
            return GitHistoryResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "历史页码必须大于或等于零，每页数量必须在 1 到 500 之间。");
        }

        GitHistoryFilter filter = request.Filter ?? new();
        string? normalizedPath = null;
        if (!string.IsNullOrWhiteSpace(filter.FilePath)
            && !GitPathValidator.TryNormalizeRelativePath(
                repositoryRoot!,
                filter.FilePath,
                out normalizedPath,
                out _))
        {
            return GitHistoryResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "历史筛选的文件路径越过了仓库边界。");
        }

        string? startingRevision = null;
        bool exactHash = !string.IsNullOrWhiteSpace(filter.Hash);
        if (exactHash)
        {
            if (!Regex.IsMatch(filter.Hash!, "^[0-9a-fA-F]{4,40}$", RegexOptions.CultureInvariant))
            {
                return GitHistoryResult.Success(new(
                    request.Page,
                    request.PageSize,
                    request.Page > 0,
                    false,
                    []));
            }

            startingRevision = await ResolveRevisionAsync(
                repositoryRoot!,
                filter.Hash!,
                cancellationToken).ConfigureAwait(false);
            if (startingRevision is null)
            {
                return GitHistoryResult.Success(new(
                    request.Page,
                    request.PageSize,
                    request.Page > 0,
                    false,
                    []));
            }
        }
        else if (!string.IsNullOrWhiteSpace(filter.Branch))
        {
            startingRevision = await ResolveRevisionAsync(
                repositoryRoot!,
                filter.Branch!,
                cancellationToken).ConfigureAwait(false);
            if (startingRevision is null)
            {
                return GitHistoryResult.Success(new(
                    request.Page,
                    request.PageSize,
                    request.Page > 0,
                    false,
                    []));
            }
        }

        int maximumCount = exactHash ? 1 : request.PageSize + 1;
        int skip = exactHash ? request.Page : checked(request.Page * request.PageSize);
        List<string> arguments =
        [
            "log",
            // 请求拓扑序：界面按 %P 返回的父子关系推导泳道，
            // 必须保证「父提交不会出现在其子提交之前」，否则推导会画出回边。
            //
            // git log 的默认顺序是按提交时间排，父的时间戳可能晚于子
            // （rebase、cherry-pick、合并都会造成这种偏斜），
            // 此时默认顺序会违反上述前提——实测构造偏斜时间戳后出现回边。
            // 代价是 Git 需要遍历完整历史：10 万提交仓库实测 31 ms -> 271 ms。
            // 提交图正确性优先于这 240 毫秒，因此保留该开关。
            "--topo-order",
            // 不请求 --graph：界面自行推导泳道，entry.Graph 没有消费方，
            // 而该开关在 10 万提交仓库上额外增加约 250 毫秒。
            "--decorate=full",
            $"--max-count={maximumCount.ToString(CultureInfo.InvariantCulture)}",
            $"--skip={skip.ToString(CultureInfo.InvariantCulture)}",
            $"--format={RecordSeparator}%H{FieldSeparator}%h{FieldSeparator}%P{FieldSeparator}%an{FieldSeparator}%ae{FieldSeparator}%aI{FieldSeparator}%s{FieldSeparator}%D",
        ];
        if (!string.IsNullOrWhiteSpace(filter.Message))
        {
            arguments.Add("--fixed-strings");
            arguments.Add("--regexp-ignore-case");
            arguments.Add($"--grep={filter.Message}");
        }

        if (!string.IsNullOrWhiteSpace(filter.Author))
        {
            arguments.Add("--regexp-ignore-case");
            arguments.Add($"--author={Regex.Escape(filter.Author)}");
        }

        if (filter.Since is not null)
        {
            arguments.Add($"--since={filter.Since.Value.ToString("O", CultureInfo.InvariantCulture)}");
        }

        if (filter.Until is not null)
        {
            arguments.Add($"--until={filter.Until.Value.ToString("O", CultureInfo.InvariantCulture)}");
        }

        arguments.Add(startingRevision ?? "--all");
        if (normalizedPath is not null)
        {
            arguments.Add("--");
            arguments.Add(normalizedPath);
        }

        GitCommandResult result = await RunQueryAsync(
            repositoryRoot!,
            arguments,
            _queryRunner,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return HistoryFailure(result);
        }

        if (result.IsOutputTruncated)
        {
            return GitHistoryResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "当前历史页输出超过 8 MB，请缩小每页数量或增加筛选条件。");
        }

        if (!TryParseHistory(result.StandardOutput, out IReadOnlyList<GitHistoryEntry>? parsed))
        {
            return GitHistoryResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "无法解析 Git 提交历史。");
        }

        bool hasNextPage = !exactHash && parsed!.Count > request.PageSize;
        GitHistoryEntry[] entries = parsed!.Take(request.PageSize).ToArray();
        return GitHistoryResult.Success(new(
            request.Page,
            request.PageSize,
            request.Page > 0,
            hasNextPage,
            entries));
    }

    /// <summary>
    /// 读取本地上游尚未包含的提交（规格 §7.12 的 Push 预览）。
    ///
    /// 没有配置上游时不报错，而是返回空列表并说明原因——
    /// 界面据此保留「定义远端」入口并禁用推送，而不是显示一个失败。
    /// </summary>
    public async Task<GitUnpushedResult> ReadUnpushedAsync(
        GitRepositorySnapshot repository,
        int maximum = 200,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? validationError))
        {
            return GitUnpushedResult.Failure(GitOperationFailureKind.InvalidRequest, validationError!);
        }

        // 先解析上游简称：没有上游时直接返回可读说明，
        // 界面据此保留「定义远端」入口并禁用推送（规格 §7.12）。
        GitCommandResult upstreamResult = await RunQueryAsync(
            repositoryRoot!,
            ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"],
            _queryRunner,
            cancellationToken).ConfigureAwait(false);
        string upstreamName = upstreamResult.IsSuccess ? upstreamResult.StandardOutput.Trim() : string.Empty;
        if (upstreamName.Length == 0)
        {
            return GitUnpushedResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "当前分支没有配置上游，无法生成推送预览。");
        }

        List<string> arguments =
        [
            "log",
            "--topo-order",
            $"--max-count={Math.Clamp(maximum, 1, 500)}",
            $"--format={RecordSeparator}%H{FieldSeparator}%h{FieldSeparator}%P{FieldSeparator}%an{FieldSeparator}%ae{FieldSeparator}%aI{FieldSeparator}%s{FieldSeparator}%D",
            "@{u}..HEAD",
        ];
        GitCommandResult result = await RunQueryAsync(
            repositoryRoot!,
            arguments,
            _queryRunner,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return GitUnpushedResult.Failure(
                GitOperationFailureKind.CommandFailed,
                result.ErrorMessage ?? "无法读取待推送提交。");
        }

        if (!TryParseHistory(result.StandardOutput, out IReadOnlyList<GitHistoryEntry>? parsed))
        {
            return GitUnpushedResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "无法解析待推送提交。");
        }

        return GitUnpushedResult.Success(parsed!, upstreamName);
    }

    public async Task<GitCommitDetailsResult> ReadCommitAsync(
        GitRepositorySnapshot repository,
        string revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? validationError))
        {
            return GitCommitDetailsResult.Failure(GitOperationFailureKind.InvalidRequest, validationError!);
        }

        string? resolved = await ResolveRevisionAsync(repositoryRoot!, revision, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return GitCommitDetailsResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "指定提交不存在或不是唯一提交。");
        }

        GitCommandResult metadataResult = await RunQueryAsync(
            repositoryRoot!,
            [
                "-c",
                "core.quotePath=false",
                "show",
                "-s",
                "--decorate=full",
                "--date=iso-strict",
                "--format=%H%x00%h%x00%P%x00%an%x00%ae%x00%aI%x00%s%x00%D%x00%B",
                resolved,
            ],
            _detailsRunner,
            cancellationToken).ConfigureAwait(false);
        if (!metadataResult.IsSuccess)
        {
            return CommitFailure(metadataResult);
        }
        if (metadataResult.IsOutputTruncated)
        {
            return GitCommitDetailsResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "提交信息超过 20 MB，已停止读取。");
        }

        if (!TryParseCommitMetadata(metadataResult.StandardOutput, out GitHistoryEntry? entry, out string? body))
        {
            return GitCommitDetailsResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "无法解析提交元数据。");
        }

        GitCommandResult filesResult = await RunQueryAsync(
            repositoryRoot!,
            ["diff-tree", "--root", "--no-commit-id", "--name-status", "-r", "-z", "-M", resolved],
            _detailsRunner,
            cancellationToken).ConfigureAwait(false);
        if (!filesResult.IsSuccess)
        {
            return CommitFailure(filesResult);
        }

        if (filesResult.IsOutputTruncated
            || !TryParseChangedFiles(filesResult.StandardOutput, out IReadOnlyList<GitCommitChangedFile>? files))
        {
            return GitCommitDetailsResult.Failure(
                GitOperationFailureKind.CommandFailed,
                filesResult.IsOutputTruncated
                    ? "提交变化文件列表超过 20 MB，已停止读取。"
                    : "无法解析提交变化文件列表。");
        }

        return GitCommitDetailsResult.Success(new(entry!, body!, files!));
    }

    public async Task<GitBlameResult> ReadBlameAsync(
        GitRepositorySnapshot repository,
        string relativePath,
        string? revision = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? validationError))
        {
            return GitBlameResult.Failure(GitOperationFailureKind.InvalidRequest, validationError!);
        }

        if (!GitPathValidator.TryNormalizeRelativePath(
            repositoryRoot!,
            relativePath,
            out string? normalizedPath,
            out _))
        {
            return GitBlameResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Blame 文件路径越过了仓库边界。");
        }

        List<string> arguments = ["-c", "core.quotePath=false", "blame", "--root", "--line-porcelain"];
        if (!string.IsNullOrWhiteSpace(revision))
        {
            string? resolved = await ResolveRevisionAsync(
                repositoryRoot!,
                revision,
                cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                return GitBlameResult.Failure(
                    GitOperationFailureKind.InvalidRequest,
                    "指定 Blame 引用不存在或不是唯一提交。");
            }

            arguments.Add(resolved);
        }

        arguments.Add("--");
        arguments.Add(normalizedPath!);
        GitCommandResult result = await RunQueryAsync(
            repositoryRoot!,
            arguments,
            _detailsRunner,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return BlameFailure(result);
        }

        if (result.IsOutputTruncated || !TryParseBlame(result.StandardOutput, out IReadOnlyList<GitBlameLine>? lines))
        {
            return GitBlameResult.Failure(
                GitOperationFailureKind.CommandFailed,
                result.IsOutputTruncated
                    ? "Blame 输出超过 20 MB，已停止读取。"
                    : "无法解析 Git blame 输出。");
        }

        return GitBlameResult.Success(lines!);
    }

    public async Task<GitComparisonResult> ReadCommitFileDiffAsync(
        GitRepositorySnapshot repository,
        string revision,
        string relativePath,
        bool ignoreWhitespace = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? validationError))
        {
            return GitComparisonResult.Failure(GitOperationFailureKind.InvalidRequest, validationError!);
        }

        string? resolved = await ResolveRevisionAsync(repositoryRoot!, revision, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return GitComparisonResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "指定提交不存在或不是唯一提交。");
        }

        if (!GitPathValidator.TryNormalizeRelativePath(
            repositoryRoot!,
            relativePath,
            out string? normalizedPath,
            out _))
        {
            return GitComparisonResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "提交 diff 文件路径越过了仓库边界。");
        }

        long oldSize = await ReadBlobSizeAsync(repositoryRoot!, resolved + "^", normalizedPath!, cancellationToken).ConfigureAwait(false);
        long newSize = await ReadBlobSizeAsync(repositoryRoot!, resolved, normalizedPath!, cancellationToken).ConfigureAwait(false);
        if (oldSize > MaximumSideBytes || newSize > MaximumSideBytes)
        {
            return GitComparisonResult.Success(new(GitDiffContentStatus.SideTooLarge,
                revision + "^", revision, normalizedPath, null));
        }

        List<string> arguments =
        [
            "-c", "core.quotePath=false", "show", "--format=", "--root", "--diff-merges=first-parent",
            "--no-ext-diff", "--no-textconv", "--find-renames=50%", "--full-index", FullFileContextArgument,
        ];
        if (ignoreWhitespace) arguments.Add("--ignore-all-space");
        arguments.AddRange([resolved, "--", normalizedPath!]);
        GitCommandResult result = await RunQueryAsync(
            repositoryRoot!,
            arguments,
            _detailsRunner,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return ComparisonFailure(result);
        }

        GitDiffContentStatus status = result.IsOutputTruncated
            ? GitDiffContentStatus.OutputTooLarge
            : IsBinaryPatch(result.StandardOutput)
                ? GitDiffContentStatus.Binary
                : GitDiffContentStatus.Ready;
        return GitComparisonResult.Success(new(
            status,
            $"{revision}^",
            revision,
            normalizedPath,
            status == GitDiffContentStatus.Ready ? result.StandardOutput : null));
    }

    public async Task<GitComparisonResult> CompareAsync(
        GitRepositorySnapshot repository,
        GitComparisonRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? validationError))
        {
            return GitComparisonResult.Failure(GitOperationFailureKind.InvalidRequest, validationError!);
        }

        string? baseRevision = await ResolveRevisionAsync(
            repositoryRoot!,
            request.BaseRevision,
            cancellationToken).ConfigureAwait(false);
        if (baseRevision is null)
        {
            return GitComparisonResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "比较的基准引用不存在或不是唯一提交。");
        }

        string? targetRevision = null;
        if (!string.IsNullOrWhiteSpace(request.TargetRevision))
        {
            targetRevision = await ResolveRevisionAsync(
                repositoryRoot!,
                request.TargetRevision,
                cancellationToken).ConfigureAwait(false);
            if (targetRevision is null)
            {
                return GitComparisonResult.Failure(
                    GitOperationFailureKind.InvalidRequest,
                    "比较的目标引用不存在或不是唯一提交。");
            }
        }

        string? normalizedPath = null;
        string? fullPath = null;
        if (!string.IsNullOrWhiteSpace(request.RelativePath)
            && !GitPathValidator.TryNormalizeRelativePath(
                repositoryRoot!,
                request.RelativePath,
                out normalizedPath,
                out fullPath))
        {
            return GitComparisonResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "比较文件路径越过了仓库边界。");
        }

        if (normalizedPath is not null)
        {
            long baseSize = await ReadBlobSizeAsync(
                repositoryRoot!,
                baseRevision,
                normalizedPath,
                cancellationToken).ConfigureAwait(false);
            long targetSize = targetRevision is null
                ? File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0
                : await ReadBlobSizeAsync(
                    repositoryRoot!,
                    targetRevision,
                    normalizedPath,
                    cancellationToken).ConfigureAwait(false);
            if (baseSize > MaximumSideBytes || targetSize > MaximumSideBytes)
            {
                return GitComparisonResult.Success(new(
                    GitDiffContentStatus.SideTooLarge,
                    request.BaseRevision,
                    request.TargetRevision,
                    normalizedPath,
                    null));
            }
        }

        List<string> arguments =
        [
            "-c",
            "core.quotePath=false",
            "diff",
            "--no-ext-diff",
            "--no-textconv",
            "--find-renames=50%",
            "--full-index",
            FullFileContextArgument,
        ];
        if (request.IgnoreWhitespace)
        {
            arguments.Add("--ignore-all-space");
        }

        arguments.Add(baseRevision);
        if (targetRevision is not null)
        {
            arguments.Add(targetRevision);
        }

        if (normalizedPath is not null)
        {
            arguments.Add("--");
            arguments.Add(normalizedPath);
        }

        GitCommandResult result = await RunQueryAsync(
            repositoryRoot!,
            arguments,
            _detailsRunner,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return ComparisonFailure(result);
        }

        GitDiffContentStatus status = result.IsOutputTruncated
            ? GitDiffContentStatus.OutputTooLarge
            : IsBinaryPatch(result.StandardOutput)
                ? GitDiffContentStatus.Binary
                : GitDiffContentStatus.Ready;
        return GitComparisonResult.Success(new(
            status,
            request.BaseRevision,
            request.TargetRevision,
            normalizedPath,
            status == GitDiffContentStatus.Ready ? result.StandardOutput : null));
    }

    internal static bool TryParseHistory(string output, out IReadOnlyList<GitHistoryEntry>? entries)
    {
        entries = null;
        List<GitHistoryEntry> parsed = [];
        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            int recordIndex = line.IndexOf(RecordSeparator);
            if (recordIndex < 0)
            {
                continue;
            }

            string graph = line[..recordIndex].TrimEnd();
            string[] fields = line[(recordIndex + 1)..].Split(FieldSeparator);
            if (fields.Length != 8
                || !DateTimeOffset.TryParse(
                    fields[5],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset date))
            {
                return false;
            }

            parsed.Add(new(
                graph,
                fields[0],
                fields[1],
                SplitParents(fields[2]),
                fields[3],
                fields[4],
                date,
                fields[6],
                ParseReferences(fields[7])));
        }

        entries = parsed;
        return true;
    }

    internal static bool TryParseChangedFiles(
        string output,
        out IReadOnlyList<GitCommitChangedFile>? files)
    {
        files = null;
        string[] fields = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        List<GitCommitChangedFile> parsed = [];
        for (int index = 0; index < fields.Length; index++)
        {
            string status = fields[index];
            if (status.Length == 0 || ++index >= fields.Length)
            {
                return false;
            }

            char code = status[0];
            if (code is 'R' or 'C')
            {
                string original = NormalizePath(fields[index]);
                if (++index >= fields.Length)
                {
                    return false;
                }

                parsed.Add(new(MapChangeKind(code), NormalizePath(fields[index]), original));
            }
            else
            {
                parsed.Add(new(MapChangeKind(code), NormalizePath(fields[index]), null));
            }
        }

        files = parsed;
        return true;
    }

    internal static bool TryParseBlame(string output, out IReadOnlyList<GitBlameLine>? lines)
    {
        lines = null;
        string[] rawLines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<GitBlameLine> parsed = [];
        int index = 0;
        while (index < rawLines.Length)
        {
            string[] header = rawLines[index].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (header.Length < 3
                || header[0].Length != 40
                || !int.TryParse(header[2], NumberStyles.None, CultureInfo.InvariantCulture, out int lineNumber))
            {
                if (rawLines[index].Length == 0)
                {
                    index++;
                    continue;
                }

                return false;
            }

            string hash = header[0];
            string author = string.Empty;
            string email = string.Empty;
            long authorTime = 0;
            TimeSpan offset = TimeSpan.Zero;
            string summary = string.Empty;
            string filePath = string.Empty;
            string? content = null;
            index++;
            while (index < rawLines.Length)
            {
                string line = rawLines[index++];
                if (line.StartsWith('\t'))
                {
                    content = line[1..];
                    break;
                }

                int separator = line.IndexOf(' ');
                string key = separator < 0 ? line : line[..separator];
                string value = separator < 0 ? string.Empty : line[(separator + 1)..];
                switch (key)
                {
                    case "author":
                        author = value;
                        break;
                    case "author-mail":
                        email = value.Trim('<', '>');
                        break;
                    case "author-time":
                        _ = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out authorTime);
                        break;
                    case "author-tz":
                        offset = ParseGitOffset(value);
                        break;
                    case "summary":
                        summary = value;
                        break;
                    case "filename":
                        filePath = NormalizePath(value);
                        break;
                }
            }

            if (content is null)
            {
                return false;
            }

            DateTimeOffset date = DateTimeOffset.FromUnixTimeSeconds(authorTime).ToOffset(offset);
            parsed.Add(new(lineNumber, hash, author, email, date, summary, filePath, content));
        }

        lines = parsed;
        return true;
    }

    private async Task<string?> ResolveRevisionAsync(
        string repositoryRoot,
        string revision,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(revision) || revision.StartsWith('-'))
        {
            return null;
        }

        GitCommandResult result = await RunQueryAsync(
            repositoryRoot,
            ["rev-parse", "--verify", "--quiet", "--end-of-options", $"{revision}^{{commit}}"],
            _queryRunner,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? NullIfEmpty(result.StandardOutput) : null;
    }

    private async Task<long> ReadBlobSizeAsync(
        string repositoryRoot,
        string revision,
        string relativePath,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await RunQueryAsync(
            repositoryRoot,
            ["cat-file", "-s", $"{revision}:{relativePath}"],
            _queryRunner,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            && long.TryParse(result.StandardOutput.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long size)
            ? size
            : 0;
    }

    private static bool TryParseCommitMetadata(
        string output,
        out GitHistoryEntry? entry,
        out string? body)
    {
        entry = null;
        body = null;
        string[] fields = output.Split('\0', 9);
        if (fields.Length != 9
            || !DateTimeOffset.TryParse(
                fields[5],
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset date))
        {
            return false;
        }

        entry = new(
            "*",
            fields[0],
            fields[1],
            SplitParents(fields[2]),
            fields[3],
            fields[4],
            date,
            fields[6],
            ParseReferences(fields[7]));
        body = fields[8].TrimEnd('\r', '\n');
        return true;
    }

    private static string[] SplitParents(string parents)
    {
        return parents.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static List<GitReferenceInfo> ParseReferences(string decorations)
    {
        if (string.IsNullOrWhiteSpace(decorations))
        {
            return [];
        }

        List<GitReferenceInfo> references = [];
        foreach (string raw in decorations.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string value = raw;
            bool isHead = value.StartsWith("HEAD -> ", StringComparison.Ordinal);
            if (isHead)
            {
                value = value[8..];
            }

            if (value.StartsWith("tag: ", StringComparison.Ordinal))
            {
                value = value[5..];
            }

            GitReferenceKind kind;
            string name;
            if (value.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                kind = GitReferenceKind.LocalBranch;
                name = value[11..];
            }
            else if (value.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                kind = GitReferenceKind.RemoteBranch;
                name = value[13..];
            }
            else if (value.StartsWith("refs/tags/", StringComparison.Ordinal))
            {
                kind = GitReferenceKind.Tag;
                name = value[10..];
            }
            else
            {
                kind = GitReferenceKind.Other;
                name = value;
            }

            references.Add(new(kind, name, isHead));
        }

        return references;
    }

    private static GitChangeKind MapChangeKind(char status)
    {
        return status switch
        {
            'A' => GitChangeKind.Added,
            'D' => GitChangeKind.Deleted,
            'R' => GitChangeKind.Renamed,
            'C' => GitChangeKind.Copied,
            'T' => GitChangeKind.TypeChanged,
            'U' => GitChangeKind.Unmerged,
            _ => GitChangeKind.Modified,
        };
    }

    private static TimeSpan ParseGitOffset(string value)
    {
        if (value.Length != 5
            || (value[0] != '+' && value[0] != '-')
            || !int.TryParse(value.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
            || !int.TryParse(value.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes))
        {
            return TimeSpan.Zero;
        }

        TimeSpan offset = new(hours, minutes, 0);
        return value[0] == '-' ? -offset : offset;
    }

    private static bool TryGetRepositoryRoot(
        GitRepositorySnapshot repository,
        out string? repositoryRoot,
        out string? error)
    {
        repositoryRoot = repository.RepositoryRoot;
        error = null;
        if (repository.Kind != GitRepositoryKind.WorkingTree || string.IsNullOrWhiteSpace(repositoryRoot))
        {
            error = "历史只适用于具有工作区的 Git 仓库。";
            return false;
        }

        return true;
    }

    private Task<GitCommandResult> RunQueryAsync(
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        GitCommandRunner runner,
        CancellationToken cancellationToken)
    {
        return runner.RunAsync(
            _runtime.ExecutablePath!,
            repositoryRoot,
            arguments,
            GitCommandMode.LocalQuery,
            cancellationToken);
    }

    private static bool IsBinaryPatch(string patch)
    {
        return patch.Contains("Binary files ", StringComparison.Ordinal)
            || patch.Contains("GIT binary patch", StringComparison.Ordinal);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string? NullIfEmpty(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static GitHistoryResult HistoryFailure(GitCommandResult result)
    {
        return GitHistoryResult.Failure(NormalizeFailure(result), result.ErrorMessage);
    }

    private static GitCommitDetailsResult CommitFailure(GitCommandResult result)
    {
        return GitCommitDetailsResult.Failure(NormalizeFailure(result), result.ErrorMessage);
    }

    private static GitBlameResult BlameFailure(GitCommandResult result)
    {
        return GitBlameResult.Failure(NormalizeFailure(result), result.ErrorMessage);
    }

    private static GitComparisonResult ComparisonFailure(GitCommandResult result)
    {
        return GitComparisonResult.Failure(NormalizeFailure(result), result.ErrorMessage);
    }

    private static GitOperationFailureKind NormalizeFailure(GitCommandResult result)
    {
        return result.FailureKind == GitOperationFailureKind.None
            ? GitOperationFailureKind.CommandFailed
            : result.FailureKind;
    }
}
