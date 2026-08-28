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
            Assert.IsTrue(result.Page!.Entries.Any(entry => entry.ParentHashes.Count == 2));
            Assert.IsTrue(result.Page.Entries.Any(entry => entry.Graph.Contains('*', StringComparison.Ordinal)));
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

            Assert.IsFalse(history.IsSuccess);
            Assert.AreEqual(GitOperationFailureKind.CommandFailed, history.FailureKind);
            Assert.Contains("超过 8 MB", history.ErrorMessage!, StringComparison.Ordinal);
            Assert.IsTrue(diff.IsSuccess, diff.ErrorMessage);
            Assert.AreEqual(GitDiffContentStatus.OutputTooLarge, diff.Document!.Status);
            Assert.IsNull(diff.Document.UnifiedPatch);
            Assert.IsNotNull(diff.Document.CopyableCommand);
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
