using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitDiffServiceTests
{
    [TestMethod]
    public async Task Diff比较Head与磁盘最新内容而不是暂存快照()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "tracked.txt", "base\n", "test: base");
            await File.WriteAllTextAsync(temporary.GetPath("tracked.txt"), "staged\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "tracked.txt");
            await File.WriteAllTextAsync(temporary.GetPath("tracked.txt"), "latest\n");
            GitChangedFile changedFile = await ReadOnlyChangeAsync(runtime, repository);

            GitDiffResult result = await new GitDiffService(runtime)
                .CreateAsync(repository, changedFile, new());

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.AreEqual(GitDiffContentStatus.Ready, result.Document!.Status);
            string patch = result.Document.UnifiedPatch!;
            Assert.Contains("-base", patch, StringComparison.Ordinal);
            Assert.Contains("+latest", patch, StringComparison.Ordinal);
            Assert.DoesNotContain("+staged", patch, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public async Task 文本Diff包含完整文件上下文而不是仅显示变化附近三行()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            string[] original = Enumerable.Range(1, 100)
                .Select(index => $"line {index:D3}")
                .ToArray();
            await GitTestEnvironment.CommitFileAsync(
                runtime,
                temporary.FullPath,
                "complete.txt",
                string.Join('\n', original) + "\n",
                "test: complete diff");
            original[49] = "line 050 changed";
            await File.WriteAllTextAsync(
                temporary.GetPath("complete.txt"),
                string.Join('\n', original) + "\n");
            GitChangedFile changedFile = await ReadOnlyChangeAsync(runtime, repository);

            GitDiffResult result = await new GitDiffService(runtime)
                .CreateAsync(repository, changedFile, new());

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.AreEqual(GitDiffContentStatus.Ready, result.Document!.Status);
            Assert.Contains(" line 001", result.Document.UnifiedPatch!, StringComparison.Ordinal);
            Assert.Contains(" line 100", result.Document.UnifiedPatch!, StringComparison.Ordinal);
            Assert.Contains("+line 050 changed", result.Document.UnifiedPatch!, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public async Task 未跟踪文本和二进制分别生成文本Diff与二进制摘要()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await File.WriteAllTextAsync(temporary.GetPath("new.txt"), "new content\n");
            await File.WriteAllBytesAsync(temporary.GetPath("binary.bin"), [0, 1, 2, 3]);
            GitStatusResult status = await new GitStatusService(runtime).ReadAsync(repository);
            Assert.IsTrue(status.IsSuccess, status.ErrorMessage);
            GitChangedFile textFile = status.Snapshot!.Files.Single(file => file.RelativePath == "new.txt");
            GitChangedFile binaryFile = status.Snapshot.Files.Single(file => file.RelativePath == "binary.bin");
            GitDiffService service = new(runtime);

            GitDiffResult textDiff = await service.CreateAsync(repository, textFile, new());
            GitDiffResult binaryDiff = await service.CreateAsync(repository, binaryFile, new());

            Assert.AreEqual(GitDiffContentStatus.Ready, textDiff.Document!.Status);
            Assert.Contains("+new content", textDiff.Document.UnifiedPatch!, StringComparison.Ordinal);
            Assert.AreEqual(GitDiffContentStatus.Binary, binaryDiff.Document!.Status);
        }
    }

    [TestMethod]
    public async Task 忽略空白差异时不生成正文变化()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "space.txt", "value one\n", "test: base");
            await File.WriteAllTextAsync(temporary.GetPath("space.txt"), "value    one\n");
            GitChangedFile changedFile = await ReadOnlyChangeAsync(runtime, repository);

            GitDiffResult result = await new GitDiffService(runtime)
                .CreateAsync(repository, changedFile, new(IgnoreWhitespace: true));

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.AreEqual(string.Empty, result.Document!.UnifiedPatch);
        }
    }

    [TestMethod]
    public async Task 任一侧超过十兆时只返回大小和摘要()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            string largePath = temporary.GetPath("large.txt");
            await using (FileStream stream = new(largePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.SetLength((10L * 1024 * 1024) + 1);
            }

            GitChangedFile changedFile = await ReadOnlyChangeAsync(runtime, repository);

            GitDiffResult result = await new GitDiffService(runtime)
                .CreateAsync(repository, changedFile, new());

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.AreEqual(GitDiffContentStatus.SideTooLarge, result.Document!.Status);
            Assert.IsNull(result.Document.UnifiedPatch);
            Assert.AreEqual((10L * 1024 * 1024) + 1, result.Document.NewSize);
        }
    }

    [TestMethod]
    public async Task 生成结果超过上限时停止正文并返回摘要()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "many.txt", "before\n", "test: base");
            await File.WriteAllTextAsync(temporary.GetPath("many.txt"), string.Join('\n', Enumerable.Repeat("after", 100)));
            GitChangedFile changedFile = await ReadOnlyChangeAsync(runtime, repository);
            GitDiffService service = new(
                runtime,
                new GitCommandRunner(),
                new GitCommandRunner(TimeSpan.FromSeconds(30), 64));

            GitDiffResult result = await service.CreateAsync(repository, changedFile, new());

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.AreEqual(GitDiffContentStatus.OutputTooLarge, result.Document!.Status);
            Assert.IsNull(result.Document.UnifiedPatch);
        }
    }

    // 历史比较（规格 §7.8）：两侧都取自 Git，右侧**不是**工作区内容。
    // 这是本轮新增的能力，工作区 Diff 与引用比较仍走原来的「版本 ↔ 工作区」路径。
    [TestMethod]
    public async Task 历史比较取两个版本而不是工作区内容()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "tracked.txt", "first\n", "test: first");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "tracked.txt", "second\n", "test: second");
            // 工作区再写入第三版：历史比较必须**忽略**它，否则会误把磁盘内容当作右值。
            await File.WriteAllTextAsync(temporary.GetPath("tracked.txt"), "working\n");
            string second = await GitTestEnvironment.RevParseAsync(runtime, temporary.FullPath, "HEAD");
            string first = await GitTestEnvironment.RevParseAsync(runtime, temporary.FullPath, "HEAD^");
            GitChangedFile changedFile = new("tracked.txt", null, GitChangeGroup.Changes, GitChangeKind.Modified, false, true);

            GitDiffResult result = await new GitDiffService(runtime).CreateAsync(
                repository,
                changedFile,
                new GitDiffOptions(false, first, second));

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.AreEqual(GitDiffContentStatus.Ready, result.Document!.Status);
            string patch = result.Document.UnifiedPatch!;
            Assert.Contains("-first", patch, StringComparison.Ordinal);
            Assert.Contains("+second", patch, StringComparison.Ordinal);
            Assert.DoesNotContain("working", patch, StringComparison.Ordinal);
        }
    }

    // 首个提交没有父版本：宿主回退到空树，使"整个文件都是新增"仍能生成差异。
    [TestMethod]
    public async Task 历史比较在无父提交时回退到空树()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "first.txt", "only\n", "test: root");
            GitHistoryService history = new(runtime);
            string root = await GitTestEnvironment.RevParseAsync(runtime, temporary.FullPath, "HEAD");
            string? parent = await history.ResolveParentRevisionAsync(temporary.FullPath, root);

            Assert.AreEqual(GitHistoryService.EmptyTreeHash, parent);
            GitChangedFile changedFile = new("first.txt", null, GitChangeGroup.Changes, GitChangeKind.Modified, false, true);
            GitDiffResult result = await new GitDiffService(runtime).CreateAsync(
                repository,
                changedFile,
                new GitDiffOptions(false, parent!, root));

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.AreEqual(GitDiffContentStatus.Ready, result.Document!.Status);
            Assert.Contains("+only", result.Document.UnifiedPatch!, StringComparison.Ordinal);
        }
    }

    // 无法解析的目标版本必须如实失败，不能悄悄退化成"和工作区比"。
    [TestMethod]
    public async Task 历史比较无法解析版本时如实失败()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "tracked.txt", "base\n", "test: base");
            GitChangedFile changedFile = new("tracked.txt", null, GitChangeGroup.Changes, GitChangeKind.Modified, false, true);

            GitDiffResult result = await new GitDiffService(runtime).CreateAsync(
                repository,
                changedFile,
                new GitDiffOptions(false, "HEAD", "no-such-revision"));

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNull(result.Document);
        }
    }

    private static async Task<GitChangedFile> ReadOnlyChangeAsync(
        GitRuntimeInfo runtime,
        GitRepositorySnapshot repository)
    {
        GitStatusResult status = await new GitStatusService(runtime).ReadAsync(repository);
        Assert.IsTrue(status.IsSuccess, status.ErrorMessage);
        return status.Snapshot!.Files.Single();
    }

    private static async Task<(TemporaryDirectory Temporary, GitRuntimeInfo Runtime, GitRepositorySnapshot Repository)>
        CreateRepositoryAsync()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        TemporaryDirectory temporary = new();
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(temporary.FullPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        return (temporary, runtime, initialized.Repository!);
    }
}
