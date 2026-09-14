using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitStatusServiceTests
{
    private static readonly string[] ExpectedChangesDisplayOrder =
    [
        "docs/a2.cs",
        "docs/a10.cs",
        "src/Augit.App/z2.cs",
        "src/z2.cs",
        "z2.cs",
        "src/Augit.App/z10.cs",
    ];

    [TestMethod]
    public async Task 已暂存和后续修改合并为同一Changes文件()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "tracked.txt", "base\n", "test: base");
            await File.WriteAllTextAsync(temporary.GetPath("tracked.txt"), "staged\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "tracked.txt");
            await File.WriteAllTextAsync(temporary.GetPath("tracked.txt"), "working\n");
            await File.WriteAllTextAsync(temporary.GetPath("untracked.txt"), "new\n");

            GitStatusResult result = await new GitStatusService(runtime).ReadAsync(repository);
            GitCommandResult head = await GitTestEnvironment.RunAsync(
                runtime,
                temporary.FullPath,
                "rev-parse",
                "--verify",
                "HEAD");

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.IsTrue(head.IsSuccess, head.ErrorMessage);
            Assert.AreEqual(head.StandardOutput.Trim(), result.Snapshot!.HeadCommit);
            Assert.HasCount(2, result.Snapshot!.Files);
            GitChangedFile tracked = result.Snapshot.Files.Single(file => file.RelativePath == "tracked.txt");
            Assert.AreEqual(GitChangeGroup.Changes, tracked.Group);
            Assert.IsTrue(tracked.HasStagedChanges);
            Assert.IsTrue(tracked.HasWorkingTreeChanges);
            GitChangedFile untracked = result.Snapshot.Files.Single(file => file.RelativePath == "untracked.txt");
            Assert.AreEqual(GitChangeGroup.UnversionedFiles, untracked.Group);
            Assert.AreEqual(GitChangeKind.Untracked, untracked.Kind);
        }
    }

    [TestMethod]
    public async Task 重命名只出现一次并保留原路径()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "old.txt", "content\n", "test: base");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "mv", "--", "old.txt", "new.txt");

            GitStatusResult result = await new GitStatusService(runtime).ReadAsync(repository);

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.HasCount(1, result.Snapshot!.Files);
            Assert.AreEqual("new.txt", result.Snapshot.Files[0].RelativePath);
            Assert.AreEqual("old.txt", result.Snapshot.Files[0].OriginalRelativePath);
            Assert.AreEqual(GitChangeKind.Renamed, result.Snapshot.Files[0].Kind);
        }
    }

    [TestMethod]
    public void 无效状态输出返回稳定失败而不产生半截列表()
    {
        bool parsed = GitStatusService.TryParseStatus("M malformed\0", out IReadOnlyList<GitChangedFile>? files);

        Assert.IsFalse(parsed);
        Assert.IsNull(files);
    }

    [TestMethod]
    public void Changes按显示文件名自然顺序排列并以路径打破同名排序()
    {
        bool parsed = GitStatusService.TryParseStatus(
            " M src/Augit.App/z10.cs\0"
            + " M src/Augit.App/z2.cs\0"
            + " M docs/a10.cs\0"
            + " M docs/a2.cs\0"
            + " M z2.cs\0"
            + " M src/z2.cs\0",
            out IReadOnlyList<GitChangedFile>? files);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(files);
        CollectionAssert.AreEqual(
            ExpectedChangesDisplayOrder,
            files!.Select(file => file.RelativePath).ToArray());
    }

    [TestMethod]
    public async Task 中文和空格文件名保持原样()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await File.WriteAllTextAsync(temporary.GetPath("说明 文档.txt"), "内容\n");

            GitStatusResult result = await new GitStatusService(runtime).ReadAsync(repository);

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.AreEqual("说明 文档.txt", result.Snapshot!.Files.Single().RelativePath);
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
        return (temporary, runtime, initialized.Repository!);
    }
}
