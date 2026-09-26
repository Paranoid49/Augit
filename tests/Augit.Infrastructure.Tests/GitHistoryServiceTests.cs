using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitHistoryServiceTests
{
    [TestMethod]
    public async Task 历史分页和全部筛选条件只读取请求页()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "shared.txt", "one\n", "test: first");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "shared.txt", "two\n", "feat: searchable history");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "other.txt", "other\n", "fix: third");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "last.txt", "last\n", "docs: fourth");
            GitHistoryService service = new(runtime);

            GitHistoryResult firstPage = await service.ReadPageAsync(repository, new(PageSize: 2));
            GitHistoryResult secondPage = await service.ReadPageAsync(repository, new(Page: 1, PageSize: 2));
            GitHistoryResult byMessage = await service.ReadPageAsync(
                repository,
                new(Filter: new(Message: "searchable")));
            GitHistoryResult byAuthor = await service.ReadPageAsync(
                repository,
                new(Filter: new(Author: "Augit Tests")));
            GitHistoryResult byFile = await service.ReadPageAsync(
                repository,
                new(Filter: new(FilePath: "shared.txt")));
            GitHistoryResult byBranch = await service.ReadPageAsync(
                repository,
                new(Filter: new(Branch: firstPage.Page!.Entries[0].References
                    .First(reference => reference.IsHead).Name)));
            GitHistoryResult byHash = await service.ReadPageAsync(
                repository,
                new(Filter: new(Hash: firstPage.Page.Entries[0].ShortHash)));
            GitHistoryResult byDate = await service.ReadPageAsync(
                repository,
                new(Filter: new(
                    Since: DateTimeOffset.UtcNow.AddDays(-1),
                    Until: DateTimeOffset.UtcNow.AddDays(1))));

            Assert.IsTrue(firstPage.IsSuccess, firstPage.ErrorMessage);
            Assert.HasCount(2, firstPage.Page.Entries);
            Assert.IsTrue(firstPage.Page.HasNextPage);
            Assert.IsFalse(firstPage.Page.HasPreviousPage);
            Assert.IsTrue(secondPage.IsSuccess, secondPage.ErrorMessage);
            Assert.HasCount(2, secondPage.Page!.Entries);
            Assert.IsTrue(secondPage.Page.HasPreviousPage);
            Assert.HasCount(1, byMessage.Page!.Entries);
            Assert.Contains("searchable", byMessage.Page.Entries[0].Subject, StringComparison.Ordinal);
            Assert.HasCount(4, byAuthor.Page!.Entries);
            Assert.HasCount(2, byFile.Page!.Entries);
            Assert.HasCount(4, byBranch.Page!.Entries);
            Assert.HasCount(1, byHash.Page!.Entries);
            Assert.AreEqual(firstPage.Page.Entries[0].FullHash, byHash.Page.Entries[0].FullHash);
            Assert.HasCount(4, byDate.Page!.Entries);
        }
    }

    [TestMethod]
    public async Task 哈希筛选命中前缀全部提交并短路其它筛选()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "a.txt", "a\n", "test: 最早");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "b.txt", "b\n", "feat: 可检索");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "c.txt", "c\n", "fix: 最新");
            GitHistoryService service = new(runtime);
            GitHistoryResult all = await service.ReadPageAsync(repository, new());
            Assert.IsTrue(all.IsSuccess, all.ErrorMessage);
            string newestFull = all.Page!.Entries[0].FullHash;
            string oldestFull = all.Page.Entries[^1].FullHash;
            string oldestShort = all.Page.Entries[^1].ShortHash;
            string newestShort = all.Page.Entries[0].ShortHash;

            // ① 前缀命中"最早那条"：同时给一段不可能命中的文本筛选，仍必须返回该提交 ——
            //    权威的哈希筛选短路其它筛选（`VcsLogFiltererImpl.kt:88-101`），
            //    且显示的是提交本身而不是"从它往前看"（本例若按 revision 解释会返回 3 条）。
            GitHistoryResult shortCircuited = await service.ReadPageAsync(
                repository,
                new(Filter: new(Hash: oldestShort, Message: "不会有任何提交命中这段文字")));
            Assert.IsTrue(shortCircuited.IsSuccess, shortCircuited.ErrorMessage);
            Assert.HasCount(1, shortCircuited.Page!.Entries);
            Assert.AreEqual(oldestFull, shortCircuited.Page.Entries[0].FullHash);

            // ② 逗号分隔的多个前缀 → 命中各自的提交（权威按前缀逐个展开）。
            GitHistoryResult multiple = await service.ReadPageAsync(
                repository,
                new(Filter: new(Hash: $"{oldestShort},{newestShort}")));
            Assert.IsTrue(multiple.IsSuccess, multiple.ErrorMessage);
            Assert.HasCount(2, multiple.Page!.Entries);
            Assert.IsTrue(multiple.Page.Entries.Any(entry => entry.FullHash == oldestFull));
            Assert.IsTrue(multiple.Page.Entries.Any(entry => entry.FullHash == newestFull));

            // ③ 不足 7 位的十六进制串不算哈希前缀（权威 `fromHash` 要求 `[a-fA-F0-9]{7,64}`）：
            //    整串退回文本筛选，命中"可检索"那条，而不是被当成 revision 解析。
            GitHistoryResult fallback = await service.ReadPageAsync(
                repository,
                new(Filter: new(Hash: "abc12", Message: "可检索")));
            Assert.IsTrue(fallback.IsSuccess, fallback.ErrorMessage);
            Assert.HasCount(1, fallback.Page!.Entries);
            Assert.Contains("可检索", fallback.Page.Entries[0].Subject, StringComparison.Ordinal);

            // ④ 只要有一个词不像哈希，整串都不成立（权威 `fromHash` 对每个词都要求匹配）。
            GitHistoryResult mixed = await service.ReadPageAsync(
                repository,
                new(Filter: new(Hash: $"{oldestShort},zzz", Message: "可检索")));
            Assert.IsTrue(mixed.IsSuccess, mixed.ErrorMessage);
            Assert.HasCount(1, mixed.Page!.Entries);
            Assert.Contains("可检索", mixed.Page.Entries[0].Subject, StringComparison.Ordinal);

            // ⑤ 前缀合法但**一条都没命中**时，权威 `applyHashFilter()` 返回 null 落回普通筛选
            //    （`VcsLogFiltererImpl.kt:336-341`）⇒ 同一个文本的文本筛选仍要生效，而不是空页。
            GitHistoryResult noMatch = await service.ReadPageAsync(
                repository,
                new(Filter: new(Hash: "deadbee", Message: "可检索")));
            Assert.IsTrue(noMatch.IsSuccess, noMatch.ErrorMessage);
            Assert.HasCount(1, noMatch.Page!.Entries);
            Assert.Contains("可检索", noMatch.Page.Entries[0].Subject, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public async Task 范围筛选只取从inclusive可达而不从exclusive可达的提交()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "a.txt", "a\n", "test: 基线");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "b.txt", "b\n", "test: 第二个");
            string current = (await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "branch", "--show-current"))
                .StandardOutput.Trim();
            // 分支停在当前提交，再往前加一条：于是 `current..feature/ux` 恰好是这一条。
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-c", "feature/ux");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "c.txt", "c\n", "feat: 分支独有");
            GitHistoryService service = new(runtime);

            // 权威 `fromRange(exclusiveRef, inclusiveRef)` / `VcsLogRangeFilterImpl` 的文本 `"$before..$after"`：
            // 即 `git log <exclusive>..<inclusive>`。这正是「与当前分支比较」要看的提交集
            // （`GitCompareBranchesUi` 用 `fromRange(otherBranchName, branchName)`）。
            GitHistoryResult ahead = await service.ReadPageAsync(
                repository,
                new(Filter: new(RangeExclusive: current, RangeInclusive: "feature/ux")));
            Assert.IsTrue(ahead.IsSuccess, ahead.ErrorMessage);
            Assert.HasCount(1, ahead.Page!.Entries);
            Assert.AreEqual("feat: 分支独有", ahead.Page.Entries[0].Subject);

            // 反向范围没有任何提交（当前分支没有 feature/ux 之外的提交）。
            GitHistoryResult behind = await service.ReadPageAsync(
                repository,
                new(Filter: new(RangeExclusive: "feature/ux", RangeInclusive: current)));
            Assert.IsTrue(behind.IsSuccess, behind.ErrorMessage);
            Assert.IsEmpty(behind.Page!.Entries);

            // 只给一端时必须明确失败，而不是悄悄退化成整仓历史。
            GitHistoryResult halfRange = await service.ReadPageAsync(
                repository,
                new(Filter: new(RangeInclusive: "feature/ux")));
            Assert.IsFalse(halfRange.IsSuccess);
            StringAssert.Contains(halfRange.ErrorMessage, "两端");
        }
    }

    [TestMethod]
    public async Task 多用户筛选取任一选中用户的提交()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "a.txt", "a\n", "test: 我的提交");
            await File.WriteAllTextAsync(Path.Combine(temporary.FullPath, "b.txt"), "b\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "b.txt");
            await GitTestEnvironment.RunAsync(
                runtime, temporary.FullPath,
                "-c", "user.name=Other Person", "-c", "user.email=other@example.invalid",
                "commit", "-m", "feat: 别人的提交");
            GitHistoryService service = new(runtime);

            // 权威 `fromUserNames(listOf(me, other))` ⇒ 两个人的提交都要（git 的多个 `--author` 是"或"）。
            GitHistoryResult both = await service.ReadPageAsync(
                repository,
                new(Filter: new(Authors: ["augit-tests@example.invalid", "other@example.invalid"])));
            Assert.IsTrue(both.IsSuccess, both.ErrorMessage);
            Assert.HasCount(2, both.Page!.Entries);

            // 只选一个人 ⇒ 只有他的提交。
            GitHistoryResult onlyOther = await service.ReadPageAsync(
                repository,
                new(Filter: new(Authors: ["other@example.invalid"])));
            Assert.IsTrue(onlyOther.IsSuccess, onlyOther.ErrorMessage);
            Assert.HasCount(1, onlyOther.Page!.Entries);
            Assert.AreEqual("feat: 别人的提交", onlyOther.Page.Entries[0].Subject);
        }
    }

    [TestMethod]
    public async Task 作者集合去重并覆盖所有分支上的作者()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "base.txt", "base\n", "test: 我的提交");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-c", "other");
            await File.WriteAllTextAsync(Path.Combine(temporary.FullPath, "other.txt"), "other\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "other.txt");
            await GitTestEnvironment.RunAsync(
                runtime, temporary.FullPath,
                "-c", "user.name=Other Person", "-c", "user.email=other@example.invalid",
                "commit", "-m", "feat: 别人的提交");
            // 同一个人换过显示名：应只出现一次（按"名字 ＋ 邮箱"去重）。
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-");

            GitAuthorsResult result = await new GitHistoryService(runtime).ReadAuthorsAsync(repository);

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.HasCount(2, result.Authors);
            Assert.IsTrue(result.Authors.Any(author => author.Email == "augit-tests@example.invalid"));
            Assert.IsTrue(result.Authors.Any(author => author.Email == "other@example.invalid"));
        }
    }

    [TestMethod]
    public async Task 历史条目带回作者与提交者两组身份()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            // 作者用 `--author` 指定、提交者取仓库配置：两者不同名，
            // 正是文件历史作者列加 `*` 与 tooltip 追加 `, via {提交者}` 的条件
            // （权威 `FileHistoryPanelImpl.AuthorColumnInfo.valueOf`／`getCustomizedRenderer`，`:751-799`）。
            await File.WriteAllTextAsync(Path.Combine(temporary.FullPath, "mixed.txt"), "mixed\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "mixed.txt");
            await GitTestEnvironment.RunAsync(
                runtime, temporary.FullPath,
                "-c", "user.name=Committer Person", "-c", "user.email=committer@example.invalid",
                "commit", "--author=Author Person <author@example.invalid>", "-m", "feat: 身份两组");

            GitHistoryResult result = await new GitHistoryService(runtime).ReadPageAsync(repository, new(PageSize: 10));

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            GitHistoryEntry entry = result.Page!.Entries[0];
            Assert.AreEqual("Author Person", entry.AuthorName);
            Assert.AreEqual("author@example.invalid", entry.AuthorEmail);
            Assert.AreEqual("Committer Person", entry.CommitterName);
            Assert.AreEqual("committer@example.invalid", entry.CommitterEmail);
        }
    }

    [TestMethod]
    public async Task 多分支筛选取各分支可达提交的并集()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "base.txt", "base\n", "test: 基线");
            string main = (await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "branch", "--show-current"))
                .StandardOutput.Trim();
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-c", "feature/a");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "a.txt", "a\n", "feat: A 独有");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", main);
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-c", "feature/b");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "b.txt", "b\n", "feat: B 独有");
            GitHistoryService service = new(runtime);

            // 权威 `VcsLogFilterObject.fromBranches(branchNames)`：从**任一**匹配分支可达的提交（并集）。
            GitHistoryResult both = await service.ReadPageAsync(
                repository,
                new(Filter: new(Branches: ["feature/a", "feature/b"])));
            Assert.IsTrue(both.IsSuccess, both.ErrorMessage);
            // 并集 = 基线 ＋ A 独有 ＋ B 独有 = 3 条（两条分支各自只有一条提交）。
            Assert.HasCount(3, both.Page!.Entries);
            Assert.IsTrue(both.Page.Entries.Any(entry => entry.Subject == "feat: A 独有"));
            Assert.IsTrue(both.Page.Entries.Any(entry => entry.Subject == "feat: B 独有"));

            // 单个分支只有它自己的那条 ＋ 基线。
            GitHistoryResult single = await service.ReadPageAsync(
                repository,
                new(Filter: new(Branches: ["feature/a"])));
            Assert.IsTrue(single.IsSuccess, single.ErrorMessage);
            Assert.HasCount(2, single.Page!.Entries);
            Assert.IsTrue(single.Page.Entries.Any(entry => entry.Subject == "feat: A 独有"));
            Assert.IsFalse(single.Page.Entries.Any(entry => entry.Subject == "feat: B 独有"));

            // 一个都解析不出来时如实返回空页（不退化成整仓历史）。
            GitHistoryResult missing = await service.ReadPageAsync(
                repository,
                new(Filter: new(Branches: ["feature/a", "no-such-branch"])));
            Assert.IsTrue(missing.IsSuccess, missing.ErrorMessage);
            Assert.HasCount(2, missing.Page!.Entries);
        }
    }

    [TestMethod]
    public async Task 多路径筛选取任一命中路径的提交()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "shared.txt", "shared\n", "test: 基线");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "one.txt", "one\n", "feat: 一号");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "two.txt", "two\n", "feat: 二号");
            Directory.CreateDirectory(temporary.GetPath("nested"));
            await File.WriteAllTextAsync(temporary.GetPath("nested/three.txt"), "three\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "nested");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "commit", "-m", "feat: 目录里的三号");
            GitHistoryService service = new(runtime);

            // 权威 `VcsLogFilterObject.fromPaths(...)`：一组路径交给 Git，任一命中即算命中
            // （`git log … -- a b`）。
            GitHistoryResult both = await service.ReadPageAsync(
                repository,
                new(Filter: new(Paths: ["one.txt", "two.txt"])));
            Assert.IsTrue(both.IsSuccess, both.ErrorMessage);
            Assert.HasCount(2, both.Page!.Entries);
            Assert.IsTrue(both.Page.Entries.Any(entry => entry.Subject == "feat: 一号"));
            Assert.IsTrue(both.Page.Entries.Any(entry => entry.Subject == "feat: 二号"));
            Assert.IsFalse(both.Page.Entries.Any(entry => entry.Subject == "feat: 目录里的三号"));

            // 目录路径本身也是合法 pathspec（树里勾选目录就是这种形态）。
            GitHistoryResult directory = await service.ReadPageAsync(
                repository,
                new(Filter: new(Paths: ["nested"])));
            Assert.IsTrue(directory.IsSuccess, directory.ErrorMessage);
            Assert.HasCount(1, directory.Page!.Entries);
            Assert.AreEqual("feat: 目录里的三号", directory.Page.Entries[0].Subject);

            // 多值优先于单值，并去掉重复项（同一个路径给两次不会把它算成两个筛选）。
            GitHistoryResult deduplicated = await service.ReadPageAsync(
                repository,
                new(Filter: new(FilePath: "shared.txt", Paths: ["one.txt", "one.txt"])));
            Assert.IsTrue(deduplicated.IsSuccess, deduplicated.ErrorMessage);
            Assert.HasCount(1, deduplicated.Page!.Entries);
            Assert.AreEqual("feat: 一号", deduplicated.Page.Entries[0].Subject);

            // 其中一个路径越界 ⇒ 整个请求失败，不静默丢掉这一项。
            GitHistoryResult escaped = await service.ReadPageAsync(
                repository,
                new(Filter: new(Paths: ["one.txt", "../outside.txt"])));
            Assert.IsFalse(escaped.IsSuccess);
            Assert.AreEqual(GitOperationFailureKind.InvalidRequest, escaped.FailureKind);
        }
    }

    [TestMethod]
    public async Task 提交图包含分支合并线和本地远端标签装饰()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "base.txt", "base\n", "test: base");
            string main = (await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "branch", "--show-current"))
                .StandardOutput.Trim();
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-c", "feature");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "feature.txt", "feature\n", "feat: branch");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", main);
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "main.txt", "main\n", "feat: main");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "merge", "--no-ff", "feature", "-m", "test: merge graph");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "tag", "v1");
            string bare = temporary.GetPath("remote.git");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "init", "--bare", bare);
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "remote", "add", "origin", bare);
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "push", "-u", "origin", main);
            GitHistoryResult result = await new GitHistoryService(runtime).ReadPageAsync(repository, new());

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            // 合并提交必须带两个父提交：界面的泳道就是由父子关系推导的
            // （buildCommitGraph），不再依赖 git 的字符画输出。
            Assert.IsTrue(
                result.Page!.Entries.Any(entry => entry.ParentHashes.Count == 2),
                "应存在带两个父提交的合并提交。");
            // 泳道推导的真实前提不是「父一定排在子之前」，而是**没有回边**：
            // git log 的默认顺序按提交时间排，父的时间戳可能晚于子（例如合并提交），
            // 因此父出现在页内更早的位置是合法的。界面按 parents 画连线，
            // 只要父在子之前被消费就不会画出回边，所以这里检查「无环」。
            // 这也解释了为什么可以安全地不传 --topo-order。
            IReadOnlyList<string> order = [.. result.Page.Entries.Select(entry => entry.FullHash)];
            Dictionary<string, int> position = [];
            for (int index = 0; index < order.Count; index++)
            {
                position[order[index]] = index;
            }

            HashSet<string> visited = [];
            for (int index = 0; index < order.Count; index++)
            {
                string hash = order[index];
                foreach (string parent in result.Page.Entries[index].ParentHashes)
                {
                    if (!position.TryGetValue(parent, out int parentIndex))
                    {
                        continue;
                    }

                    Assert.DoesNotContain(
                        parent,
                        visited,
                        $"父提交 {parent[..7]} 同时是更早提交的祖先，页内顺序存在回边。");
                    Assert.AreNotEqual(
                        index,
                        parentIndex,
                        $"提交 {hash[..7]} 不能是自己的父提交。");
                }

                visited.Add(hash);
            }

            Assert.IsTrue(result.Page.Entries.SelectMany(entry => entry.References)
                .Any(reference => reference.Kind == GitReferenceKind.LocalBranch && reference.Name == main));
            Assert.IsTrue(result.Page.Entries.SelectMany(entry => entry.References)
                .Any(reference => reference.Kind == GitReferenceKind.RemoteBranch && reference.Name == $"origin/{main}"));
            Assert.IsTrue(result.Page.Entries.SelectMany(entry => entry.References)
                .Any(reference => reference.Kind == GitReferenceKind.Tag && reference.Name == "v1"));
        }
    }

    [TestMethod]
    public async Task 提交详情支持重命名并可比较提交与工作区()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(
                runtime,
                temporary.FullPath,
                "old.txt",
                "line one\nline two\nline three\n",
                "test: base");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "mv", "old.txt", "new.txt");
            await File.WriteAllTextAsync(
                temporary.GetPath("new.txt"),
                "line one\nline two changed\nline three\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "new.txt");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "commit", "-m", "feat: rename file");
            GitHistoryService service = new(runtime);

            GitCommitDetailsResult details = await service.ReadCommitAsync(repository, "HEAD");
            GitComparisonResult fileDiff = await service.ReadCommitFileDiffAsync(repository, "HEAD", "new.txt");
            GitComparisonResult committed = await service.CompareAsync(
                repository,
                new("HEAD^", "HEAD", "new.txt"));
            await File.WriteAllTextAsync(temporary.GetPath("new.txt"), "working tree\n");
            GitComparisonResult workingTree = await service.CompareAsync(
                repository,
                new("HEAD", RelativePath: "new.txt"));

            Assert.IsTrue(details.IsSuccess, details.ErrorMessage);
            GitCommitChangedFile changed = details.Details!.Files.Single();
            Assert.AreEqual(GitChangeKind.Renamed, changed.Kind);
            Assert.AreEqual("old.txt", changed.OriginalRelativePath);
            Assert.AreEqual("new.txt", changed.RelativePath);
            Assert.IsTrue(fileDiff.IsSuccess, fileDiff.ErrorMessage);
            Assert.Contains("new.txt", fileDiff.Document!.UnifiedPatch!, StringComparison.Ordinal);
            Assert.IsTrue(committed.IsSuccess, committed.ErrorMessage);
            Assert.AreEqual(GitDiffContentStatus.Ready, committed.Document!.Status);
            Assert.Contains("new", committed.Document.UnifiedPatch!, StringComparison.Ordinal);
            Assert.IsTrue(workingTree.IsSuccess, workingTree.ErrorMessage);
            Assert.Contains("working tree", workingTree.Document!.UnifiedPatch!, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public async Task 提交文件和引用比较都返回完整文件上下文()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            string[] lines = Enumerable.Range(1, 100)
                .Select(index => $"line {index:D3}")
                .ToArray();
            await GitTestEnvironment.CommitFileAsync(
                runtime,
                temporary.FullPath,
                "complete.txt",
                string.Join('\n', lines) + "\n",
                "test: complete base");
            lines[49] = "line 050 changed";
            await File.WriteAllTextAsync(
                temporary.GetPath("complete.txt"),
                string.Join('\n', lines) + "\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "complete.txt");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "commit", "-m", "test: complete changed");
            GitHistoryService service = new(runtime);

            GitComparisonResult commitDiff = await service.ReadCommitFileDiffAsync(
                repository,
                "HEAD",
                "complete.txt");
            GitComparisonResult comparison = await service.CompareAsync(
                repository,
                new("HEAD^", "HEAD", "complete.txt"));

            foreach (GitComparisonResult result in new[] { commitDiff, comparison })
            {
                Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
                Assert.AreEqual(GitDiffContentStatus.Ready, result.Document!.Status);
                Assert.Contains(" line 001", result.Document.UnifiedPatch!, StringComparison.Ordinal);
                Assert.Contains(" line 100", result.Document.UnifiedPatch!, StringComparison.Ordinal);
                Assert.Contains("+line 050 changed", result.Document.UnifiedPatch!, StringComparison.Ordinal);
            }
        }
    }

    [TestMethod]
    public async Task 文件历史和Blame返回逐行提交来源()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "lines.txt", "first\nsecond\n", "test: lines");
            await File.WriteAllTextAsync(temporary.GetPath("lines.txt"), "first changed\nsecond\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "lines.txt");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "commit", "-m", "feat: first line");
            GitHistoryService service = new(runtime);

            GitHistoryResult history = await service.ReadPageAsync(
                repository,
                new(Filter: new(FilePath: "lines.txt")));
            GitBlameResult blame = await service.ReadBlameAsync(repository, "lines.txt");

            Assert.IsTrue(history.IsSuccess, history.ErrorMessage);
            Assert.HasCount(2, history.Page!.Entries);
            Assert.IsTrue(blame.IsSuccess, blame.ErrorMessage);
            Assert.HasCount(2, blame.Lines!);
            Assert.AreEqual("first changed", blame.Lines![0].Content);
            Assert.AreEqual("second", blame.Lines[1].Content);
            Assert.AreNotEqual(blame.Lines[0].CommitHash, blame.Lines[1].CommitHash);
        }
    }

    [TestMethod]
    public async Task 空仓库历史返回空页且越界文件筛选被拒绝()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            GitHistoryService service = new(runtime);

            GitHistoryResult empty = await service.ReadPageAsync(repository, new());
            GitHistoryResult escaped = await service.ReadPageAsync(
                repository,
                new(Filter: new(FilePath: "../outside.txt")));

            Assert.IsTrue(empty.IsSuccess, empty.ErrorMessage);
            Assert.IsEmpty(empty.Page!.Entries);
            Assert.IsFalse(escaped.IsSuccess);
            Assert.AreEqual(GitOperationFailureKind.InvalidRequest, escaped.FailureKind);
        }
    }

    [TestMethod]
    public async Task 根提交和中文路径可读取详情Blame与原始文件Diff()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(
                runtime,
                temporary.FullPath,
                "中文文件.txt",
                "第一行\n第二行\n",
                "test: 中文根提交");
            GitHistoryService service = new(runtime);

            GitCommitDetailsResult details = await service.ReadCommitAsync(repository, "HEAD");
            GitComparisonResult diff = await service.ReadCommitFileDiffAsync(repository, "HEAD", "中文文件.txt");
            GitBlameResult blame = await service.ReadBlameAsync(repository, "中文文件.txt", "HEAD");

            Assert.IsTrue(details.IsSuccess, details.ErrorMessage);
            Assert.HasCount(1, details.Details!.Files);
            Assert.AreEqual("中文文件.txt", details.Details.Files[0].RelativePath);
            Assert.IsTrue(diff.IsSuccess, diff.ErrorMessage);
            Assert.AreEqual(GitDiffContentStatus.Ready, diff.Document!.Status);
            Assert.Contains("中文文件.txt", diff.Document.UnifiedPatch!, StringComparison.Ordinal);
            Assert.Contains("第一行", diff.Document.UnifiedPatch!, StringComparison.Ordinal);
            Assert.IsTrue(blame.IsSuccess, blame.ErrorMessage);
            Assert.HasCount(2, blame.Lines!);
            Assert.IsTrue(blame.Lines!.All(line => line.RelativePath == "中文文件.txt"));
        }
    }

    [TestMethod]
    public async Task 历史页和比较输出超限时返回稳定边界结果()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            string content = string.Concat(Enumerable.Repeat("很长的中文内容", 300));
            await GitTestEnvironment.CommitFileAsync(
                runtime,
                temporary.FullPath,
                "large.txt",
                content,
                $"test: {new string('x', 400)}");
            GitHistoryService service = new(
                runtime,
                new GitCommandRunner(TimeSpan.FromSeconds(30), 128),
                new GitCommandRunner(TimeSpan.FromSeconds(30), 128));

            GitHistoryResult history = await service.ReadPageAsync(repository, new());
            GitComparisonResult diff = await service.ReadCommitFileDiffAsync(repository, "HEAD", "large.txt");
            GitCommitDetailsResult details = await service.ReadCommitAsync(repository, "HEAD");

            Assert.IsFalse(history.IsSuccess);
            Assert.AreEqual(GitOperationFailureKind.CommandFailed, history.FailureKind);
            Assert.Contains("超过 8 MB", history.ErrorMessage!, StringComparison.Ordinal);
            Assert.IsTrue(diff.IsSuccess, diff.ErrorMessage);
            Assert.AreEqual(GitDiffContentStatus.OutputTooLarge, diff.Document!.Status);
            Assert.IsNull(diff.Document.UnifiedPatch);
            Assert.IsFalse(details.IsSuccess);
            Assert.Contains("提交信息超过 20 MB", details.ErrorMessage!, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public async Task 文件历史忽略空白支持根提交以及纯空白改动()
    {
        var (temporary, runtime, repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "中文.txt", "alpha beta\n", "test: 根提交");
            GitHistoryService service = new(runtime);
            GitComparisonResult root = await service.ReadCommitFileDiffAsync(repository, "HEAD", "中文.txt", ignoreWhitespace: true);
            Assert.IsTrue(root.IsSuccess, root.ErrorMessage);
            Assert.Contains("+alpha beta", root.Document!.UnifiedPatch!);
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "中文.txt", "alpha   beta\n", "test: 空白改动");
            GitComparisonResult normal = await service.ReadCommitFileDiffAsync(repository, "HEAD", "中文.txt");
            GitComparisonResult ignored = await service.ReadCommitFileDiffAsync(repository, "HEAD", "中文.txt", ignoreWhitespace: true);
            Assert.IsTrue(normal.IsSuccess, normal.ErrorMessage);
            Assert.IsTrue(ignored.IsSuccess, ignored.ErrorMessage);
            Assert.Contains("+alpha   beta", normal.Document!.UnifiedPatch!);
            Assert.AreEqual(string.Empty, ignored.Document!.UnifiedPatch);
            Assert.AreEqual(normal.Document.BaseRevision, ignored.Document.BaseRevision);
            Assert.AreEqual(normal.Document.TargetRevision, ignored.Document.TargetRevision);
        }
    }

    [TestMethod]
    public async Task 文件历史合并提交使用第一父版本的普通双侧补丁()
    {
        var (temporary, runtime, repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "a.txt", "原文\n", "test: 根提交");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "checkout", "-b", "topic");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "a.txt", "分支内容\n", "test: 分支变更");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "checkout", "-");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "merge", "--no-ff", "topic", "-m", "test: 合并");
            GitComparisonResult result = await new GitHistoryService(runtime).ReadCommitFileDiffAsync(repository, "HEAD", "a.txt");
            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.Contains("-原文", result.Document!.UnifiedPatch!);
            Assert.Contains("+分支内容", result.Document.UnifiedPatch!);
            Assert.DoesNotContain("@@@", result.Document.UnifiedPatch!);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task 文件历史任意一侧超过十MB时返回摘要不读取补丁(bool oldSide)
    {
        var (temporary, runtime, repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            string large = new('a', 10 * 1024 * 1024 + 1);
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "large.txt", oldSide ? large : "small\n", "test: 原版本");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "large.txt", oldSide ? "small\n" : large, "test: 新版本");
            GitComparisonResult result = await new GitHistoryService(runtime)
                .ReadCommitFileDiffAsync(repository, "HEAD", "large.txt");
            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.AreEqual(GitDiffContentStatus.SideTooLarge, result.Document!.Status);
            Assert.IsNull(result.Document.UnifiedPatch);
        }
    }

    [TestMethod]
    public async Task 没有上游时待推送读取返回可读原因而不是失败()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "a.txt", "a\n", "test: first");

            GitUnpushedResult result = await new GitHistoryService(runtime).ReadUnpushedAsync(repository);

            Assert.IsFalse(result.IsSuccess);
            // 原因必须说清是「没有上游」，界面据此保留定义远端入口。
            StringAssert.Contains(result.ErrorMessage, "上游");
        }
    }

    [TestMethod]
    public async Task 待推送只包含上游之后的提交()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            // 用一个本地裸仓库充当远端，避免测试依赖网络。
            // 放在临时目录内部：TemporaryDirectory.Dispose 会先把文件设为可写再删除，
            // 否则裸仓库里只读的 Git 对象会让清理失败。
            string remotePath = temporary.GetPath("remote.git");
            Directory.CreateDirectory(remotePath);
            await GitTestEnvironment.RunAsync(runtime, remotePath, "init", "--bare");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "a.txt", "a\n", "test: 已推送");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "remote", "add", "origin", remotePath);
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "push", "-u", "origin", "HEAD");

            // 上游之后新增两条：只有这两条应出现在待推送列表里。
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "b.txt", "b\n", "feat: 待推送一");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "c.txt", "c\n", "feat: 待推送二");

            GitUnpushedResult result = await new GitHistoryService(runtime).ReadUnpushedAsync(repository);

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.IsNotNull(result.Commits);
            Assert.HasCount(2, result.Commits!);
            Assert.AreEqual("feat: 待推送二", result.Commits![0].Subject);
            Assert.AreEqual("feat: 待推送一", result.Commits![1].Subject);
            Assert.IsFalse(
                result.Commits!.Any(commit => commit.Subject == "test: 已推送"),
                "已推送到上游的提交不得出现在待推送列表里。");

        }
    }

    private static async Task<(TemporaryDirectory Temporary, GitRuntimeInfo Runtime, GitRepositorySnapshot Repository)>
        CreateRepositoryAsync()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        TemporaryDirectory temporary = new();
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(temporary.FullPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(
            runtime,
            temporary.FullPath,
            "config",
            "user.email",
            "augit-tests@example.invalid");
        return (temporary, runtime, initialized.Repository!);
    }
}
