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
    public async Task 任一侧超过十兆时只返回大小和可复制命令()
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
            Assert.IsNotNull(result.Document.CopyableCommand);
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
            Assert.IsNotNull(result.Document.CopyableCommand);
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
