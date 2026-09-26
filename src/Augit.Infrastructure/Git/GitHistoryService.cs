using System.Globalization;
using System.Text.RegularExpressions;
using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

public sealed class GitHistoryService : IGitHistoryService
{
    private const char RecordSeparator = '\x1e';
    private const char FieldSeparator = '\x1f';

    /// <summary>
    /// 历史条目的一行格式（与 `TryParseHistory` 的解析顺序一一对应）。
    /// 作者与提交者都取：文件历史的作者列按权威 `FileHistoryPanelImpl.AuthorColumnInfo`
    /// 用「作者 ≠ 提交者」决定 `*` 与 `, via {提交者}` 的 tooltip。
    /// </summary>
    private static readonly string HistoryFormat =
        $"{RecordSeparator}%H{FieldSeparator}%h{FieldSeparator}%P{FieldSeparator}%an{FieldSeparator}%ae{FieldSeparator}%cn{FieldSeparator}%ce{FieldSeparator}%aI{FieldSeparator}%s{FieldSeparator}%D";
    private const int MaximumHistoryOutputBytes = 8 * 1024 * 1024;

    /// <summary>
    /// 哈希筛选一次最多展开的提交数（安全上限：前缀越短命中的对象越多，
    /// 而 `--disambiguate` 会给出**所有**以该前缀开头的对象）。
    /// </summary>
    private const int MaximumHashMatches = 500;
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

    /// <summary>
    /// 读取作者集合（权威 `VcsLogUserResolver`／`GitUserRegistry` 从日志收集用户）：
    /// `git log --branches --remotes --format=%an%x1f%ae` 去重后按名字排序。
    /// 只回"历史里出现过的人"，不猜配置里的默认用户。
    /// </summary>
    public async Task<GitAuthorsResult> ReadAuthorsAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? validationError))
        {
            return GitAuthorsResult.Failure(GitOperationFailureKind.InvalidRequest, validationError!);
        }

        GitCommandResult result = await RunQueryAsync(
            repositoryRoot!,
            [
                "-c",
                "core.quotePath=false",
                "log",
                "--branches",
                "--remotes",
                $"--format=%an{FieldSeparator}%ae",
            ],
            _queryRunner,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return GitAuthorsResult.Failure(NormalizeFailure(result), result.ErrorMessage);
        }

        Dictionary<string, GitAuthorInfo> byKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = rawLine.TrimEnd('\r').Split(FieldSeparator);
            if (fields.Length != 2)
            {
                continue;
            }

            string name = fields[0].Trim();
            string email = fields[1].Trim();
            if (name.Length == 0 && email.Length == 0)
            {
                continue;
            }

            // 同一个人可能换过显示名：以"名字 ＋ 邮箱"为键去重，避免把同一邮箱列两次。
            string key = $"{name}\u0000{email}";
            byKey.TryAdd(key, new GitAuthorInfo(name, email));
        }

        GitAuthorInfo[] authors = byKey.Values
            .OrderBy(author => author.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(author => author.Email, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return GitAuthorsResult.Success(authors);
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
        // 路径筛选：权威 `VcsLogStructureFilter` 持有的是一组 `FilePath`
        // （`VcsLogFilterObject.fromPaths(...)`，`platform/vcs-log/impl/src/com/intellij/vcs/log/ui/filter/FileFilterModel.kt:76-83`），
        // 用户可以在「选择…」里一次给多行、或在树里一次勾多个目录/文件。
        // `Paths` 优先于单值 `FilePath`（文件历史面板给单值）。
        IReadOnlyList<string> requestedPaths = filter.Paths is { Count: > 0 }
            ? filter.Paths
            : string.IsNullOrWhiteSpace(filter.FilePath) ? [] : [filter.FilePath!];
        List<string> normalizedPaths = [];
        foreach (string requestedPath in requestedPaths)
        {
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                continue;
            }

            if (!GitPathValidator.TryNormalizeRelativePath(
                    repositoryRoot!,
                    requestedPath,
                    out string? normalized,
                    out _))
            {
                return GitHistoryResult.Failure(
                    GitOperationFailureKind.InvalidRequest,
                    "历史筛选的文件路径越过了仓库边界。");
            }

            if (normalized is not null && !normalizedPaths.Contains(normalized, StringComparer.Ordinal))
            {
                normalizedPaths.Add(normalized);
            }
        }

        // 哈希筛选：权威 `VcsLogFiltererImpl.filter()`
        // （`platform/vcs-log/impl/src/com/intellij/vcs/log/visible/VcsLogFiltererImpl.kt:88-101`）
        // 在存在哈希筛选时**短路**其它全部筛选（该处原注释："hashes should be shown,
        // no matter if they match other filters or not"），且匹配的是「完整哈希以该前缀开头的**所有**提交」
        // （同文件 `:323-335` 的 `iterateCommitsWithPrefix`）。
        // 界面的「文本或哈希」会同时送出文本与哈希两种筛选（`TextFilterModel.setFilterText`，
        // `platform/vcs-log/impl/src/com/intellij/vcs/log/ui/filter/TextFilterModel.kt:96-103`），
        // 因此这里必须先判哈希：否则 `--grep=<哈希>` 会把命中的提交也筛掉。
        string[] hashPrefixes = ParseHashPrefixes(filter.Hash);
        List<string> hashRevisions = hashPrefixes.Length > 0
            ? await ExpandHashPrefixesAsync(repositoryRoot!, hashPrefixes, cancellationToken).ConfigureAwait(false)
            : [];
        // 权威 `applyHashFilter()` 在前缀**一条都没命中**时 `return null`（同文件 `:336-341`），
        // 于是落回普通筛选路径 —— 此时同一个文本还带着文本筛选（`--grep`）在起作用。
        // 只有"命中非空"才短路其它筛选。
        bool hashMode = hashRevisions.Count > 0;
        List<string> arguments;
        if (hashMode)
        {
            arguments =
            [
                "log",
                // `--no-walk=unsorted`：只列给定提交、不遍历祖先。
                //
                // **不能带 `--max-count`**：实测（Windows Git 2.45.1 与 Linux Git 2.43.0 同样）
                // 它会让 `--no-walk` 失效并沿祖先继续遍历——只给 a 与 c 两条会把中间的 b 也列出来；
                // `--skip` 无此问题。**也不能用 `--no-walk=sorted`**（同样是遍历行为）。
                // 数量上限与分页因此都放在宿主侧：见下面的排序与 `MaximumHashMatches`。
                "--no-walk=unsorted",
                // `--ignore-missing`：`--disambiguate` 也会给出树/blob，非提交对象由 Git 忽略。
                "--ignore-missing",
                "--decorate=full",
                $"--format={HistoryFormat}",
            ];
            arguments.AddRange(hashRevisions);
        }
        else
        {
            // 起始修订集合（权威：分支筛选给出的是**一组**起点，`--all` 是"没有任何筛选"时的全集）。
            List<string> startingRevisions = [];
            // 范围筛选（权威 `VcsLogRangeFilter`，`VcsLogFilterObject.fromRange(exclusiveRef, inclusiveRef)`）：
            // 取从 inclusive 可达、但不从 exclusive 可达的提交，即 `git log <exclusive>..<inclusive>`。
            // 两端必须同时给出（权威的 `RefRange` 天生是一对）。
            bool hasRange = !string.IsNullOrWhiteSpace(filter.RangeExclusive)
                || !string.IsNullOrWhiteSpace(filter.RangeInclusive);
            if (hasRange)
            {
                if (string.IsNullOrWhiteSpace(filter.RangeExclusive)
                    || string.IsNullOrWhiteSpace(filter.RangeInclusive))
                {
                    return GitHistoryResult.Failure(
                        GitOperationFailureKind.InvalidRequest,
                        "范围筛选需要同时给出两端引用。");
                }

                string? exclusiveRevision = await ResolveRevisionAsync(
                    repositoryRoot!,
                    filter.RangeExclusive!,
                    cancellationToken).ConfigureAwait(false);
                string? inclusiveRevision = await ResolveRevisionAsync(
                    repositoryRoot!,
                    filter.RangeInclusive!,
                    cancellationToken).ConfigureAwait(false);
                if (exclusiveRevision is null || inclusiveRevision is null)
                {
                    return EmptyPage(request);
                }

                // 解析成完整哈希再拼范围，避免短名在 `A..B` 里的歧义。
                startingRevisions.Add($"{exclusiveRevision}..{inclusiveRevision}");
            }
            else
            {
                // 分支筛选：权威 `VcsLogFilterObject.fromBranches(branchNames)` —— 取从**任一**匹配分支
                // 可达的提交（并集），对应 `git log b1 b2 …`；`Branches` 优先于单值 `Branch`。
                IReadOnlyList<string> branchNames = filter.Branches is { Count: > 0 }
                    ? filter.Branches
                    : string.IsNullOrWhiteSpace(filter.Branch) ? [] : [filter.Branch!];
                foreach (string branchName in branchNames)
                {
                    string? resolved = await ResolveRevisionAsync(
                        repositoryRoot!,
                        branchName,
                        cancellationToken).ConfigureAwait(false);
                    if (resolved is not null)
                    {
                        startingRevisions.Add(resolved);
                    }
                }

                if (branchNames.Count > 0 && startingRevisions.Count == 0)
                {
                    // 一个都解析不出来（含"引用存在但解析失败"）：如实返回空页，不退化成整仓历史。
                    return EmptyPage(request);
                }
            }

            arguments =
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
                $"--max-count={(request.PageSize + 1).ToString(CultureInfo.InvariantCulture)}",
                $"--skip={checked(request.Page * request.PageSize).ToString(CultureInfo.InvariantCulture)}",
                $"--format={HistoryFormat}",
            ];
            if (!string.IsNullOrWhiteSpace(filter.Message))
            {
                arguments.Add("--fixed-strings");
                arguments.Add("--regexp-ignore-case");
                arguments.Add($"--grep={filter.Message}");
            }

            // 用户筛选（权威 `VcsLogFilterObject.fromUserNames(values)`：一组用户）。
            // `Authors` 优先于单值 `Author`；git 的多个 `--author` 是**或**关系（与"任一选中用户提交的提交"一致）。
            IReadOnlyList<string> authors = filter.Authors is { Count: > 0 }
                ? filter.Authors
                : string.IsNullOrWhiteSpace(filter.Author) ? [] : [filter.Author!];
            if (authors.Count > 0)
            {
                arguments.Add("--regexp-ignore-case");
                foreach (string author in authors)
                {
                    arguments.Add($"--author={Regex.Escape(author)}");
                }
            }

            if (filter.Since is not null)
            {
                arguments.Add($"--since={filter.Since.Value.ToString("O", CultureInfo.InvariantCulture)}");
            }

            if (filter.Until is not null)
            {
                arguments.Add($"--until={filter.Until.Value.ToString("O", CultureInfo.InvariantCulture)}");
            }

            if (startingRevisions.Count == 0)
            {
                arguments.Add("--all");
            }
            else
            {
                arguments.AddRange(startingRevisions);
            }
            if (normalizedPaths.Count > 0)
            {
                // 多路径就是多个 pathspec：`git log … -- p1 p2`，与权威把整组 `FilePath`
                // 交给 Git 的做法一致（任一命中即算命中）。
                arguments.Add("--");
                arguments.AddRange(normalizedPaths);
            }
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

        bool hasNextPage;
        GitHistoryEntry[] entries;
        if (hashMode)
        {
            // 权威按提交时间倒序展示命中的提交（日志的常规顺序）。哈希模式既不能用
            // `--no-walk=sorted` 也不能带 `--max-count`（都会让它沿祖先遍历，见上面的注释），
            // 因此排序、上限与分页都在宿主侧做。
            int skip = checked(request.Page * request.PageSize);
            GitHistoryEntry[] sorted = parsed!
                .OrderByDescending(entry => entry.AuthorDate)
                .Take(MaximumHashMatches)
                .ToArray();
            hasNextPage = sorted.Length > skip + request.PageSize;
            entries = sorted.Skip(skip).Take(request.PageSize).ToArray();
        }
        else
        {
            hasNextPage = parsed!.Count > request.PageSize;
            entries = parsed!.Take(request.PageSize).ToArray();
        }

        return GitHistoryResult.Success(new(
            request.Page,
            request.PageSize,
            request.Page > 0,
            hasNextPage,
            entries));
    }

    /// <summary>
    /// 解析「文本或哈希」里的哈希前缀，照权威 `VcsLogFilterObject.fromHash`
    /// （`platform/vcs-log/impl/src/com/intellij/vcs/log/visible/filters/VcsLogFilters.kt:149-160`
    /// ＋ 同文件 `:310-315` 的 `HashSeparatorCharFilter`）：按逗号、分号与空白切词，
    /// **只要有一个词不匹配** `VcsLogUtil.GIT_HASH_REGEX` = `[a-fA-F0-9]{7,64}`
    /// （`platform/vcs-log/impl/src/com/intellij/vcs/log/util/VcsLogUtil.java:92`）就整体不成立，
    /// 退回普通文本筛选；一个词都没有（空串）同样不成立。
    /// </summary>
    private static string[] ParseHashPrefixes(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return [];
        }

        string[] words = hash.Split(
            [' ', '\t', '\r', '\n', ',', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return [];
        }

        foreach (string word in words)
        {
            if (!Regex.IsMatch(word, "^[0-9a-fA-F]{7,64}$", RegexOptions.CultureInvariant))
            {
                return [];
            }
        }

        return words;
    }

    /// <summary>
    /// 把哈希前缀展开成完整对象名（去重）。权威按前缀遍历日志索引
    /// （`VcsLogFiltererImpl.kt:323-335` 的 `iterateCommitsWithPrefix`），
    /// 这里用 `rev-parse --disambiguate` 让 Git 做同一件事；命中的树/blob
    /// 由调用处的 `git log --no-walk --ignore-missing` 忽略。
    /// </summary>
    private async Task<List<string>> ExpandHashPrefixesAsync(
        string repositoryRoot,
        IReadOnlyList<string> prefixes,
        CancellationToken cancellationToken)
    {
        List<string> arguments = ["rev-parse"];
        foreach (string prefix in prefixes)
        {
            arguments.Add($"--disambiguate={prefix}");
        }

        GitCommandResult result = await RunQueryAsync(
            repositoryRoot,
            arguments,
            _queryRunner,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return [];
        }

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static GitHistoryResult EmptyPage(GitHistoryRequest request) =>
        GitHistoryResult.Success(new(
            request.Page,
            request.PageSize,
            request.Page > 0,
            false,
            []));

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
            // 与分页历史共用同一格式串：解析器只认一种字段布局（含提交者两组），
            // 两处各写一份一旦漏改就会静默解析失败。
            $"--format={HistoryFormat}",
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
                "--format=%H%x00%h%x00%P%x00%an%x00%ae%x00%cn%x00%ce%x00%aI%x00%s%x00%D%x00%B",
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
            if (fields.Length != 10
                || !DateTimeOffset.TryParse(
                    fields[7],
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
                fields[5],
                fields[6],
                date,
                fields[8],
                ParseReferences(fields[9])));
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
            // `previous <sha> <file>`：该行在更早的修订里已存在时才有 ⇒ 空串表示"没有上一修订"。
            string previousRevision = string.Empty;
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
                    case "previous":
                        // 值是 `<sha> <file>`，只取哈希；带空格的文件名不影响（只切第一个空格）。
                        int previousSeparator = value.IndexOf(' ');
                        previousRevision = previousSeparator < 0 ? value : value[..previousSeparator];
                        break;
                }
            }

            if (content is null)
            {
                return false;
            }

            DateTimeOffset date = DateTimeOffset.FromUnixTimeSeconds(authorTime).ToOffset(offset);
            parsed.Add(new(lineNumber, hash, author, email, date, summary, filePath, content, previousRevision));
        }

        lines = parsed;
        return true;
    }

    /// <summary>
    /// 解析提交的父版本，用于历史比较（规格 §7.8）。
    /// </summary>
    /// <remarks>
    /// 无父提交（仓库首个提交）时回退到 Git 的空树对象，使"整个文件都是新增"
    /// 与普通比较走同一条 diff 命令。无法解析该提交时返回 <c>null</c>，
    /// 由调用方如实报错而不是猜一个基准。
    /// </remarks>
    public async Task<string?> ResolveParentRevisionAsync(
        string repositoryRoot,
        string commit,
        CancellationToken cancellationToken = default)
    {
        string? hash = await ResolveRevisionAsync(repositoryRoot, commit, cancellationToken).ConfigureAwait(false);
        if (hash is null)
        {
            return null;
        }

        GitCommandResult parent = await RunQueryAsync(
            repositoryRoot,
            ["rev-parse", "--verify", "--quiet", "--end-of-options", $"{hash}^"],
            _queryRunner,
            cancellationToken).ConfigureAwait(false);
        return parent.IsSuccess && !string.IsNullOrWhiteSpace(parent.StandardOutput)
            ? parent.StandardOutput.Trim()
            : EmptyTreeHash;
    }

    /// <summary>Git 的空树对象哈希：表示"该侧不存在"。</summary>
    public const string EmptyTreeHash = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

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
        string[] fields = output.Split('\0', 11);
        if (fields.Length != 11
            || !DateTimeOffset.TryParse(
                fields[7],
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
            fields[5],
            fields[6],
            date,
            fields[8],
            ParseReferences(fields[9]));
        body = fields[10].TrimEnd('\r', '\n');
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
