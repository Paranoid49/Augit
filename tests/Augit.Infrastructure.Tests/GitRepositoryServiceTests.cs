using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitRepositoryServiceTests
{
    [TestMethod]
    public async Task 普通目录可识别并显式初始化()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        GitRepositoryService service = new(runtime);

        GitRepositoryOperationResult before = await service.InspectAsync(temporary.FullPath);
        GitRepositoryOperationResult initialized = await service.InitializeAsync(temporary.FullPath);

        Assert.IsTrue(before.IsSuccess, before.ErrorMessage);
        Assert.AreEqual(GitRepositoryKind.PlainDirectory, before.Repository!.Kind);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        Assert.AreEqual(GitRepositoryKind.WorkingTree, initialized.Repository!.Kind);
        Assert.AreEqual(Path.GetFullPath(temporary.FullPath), initialized.Repository.RepositoryRoot);
        Assert.IsTrue(Directory.Exists(initialized.Repository.GitDirectory));
    }

    [TestMethod]
    public async Task 本地仓库可克隆到空目标目录()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string source = temporary.GetPath("source");
        string destination = temporary.GetPath("destination");
        Directory.CreateDirectory(source);
        GitRepositoryService service = new(runtime);
        GitRepositoryOperationResult initialized = await service.InitializeAsync(source);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.CommitFileAsync(runtime, source, "readme.txt", "内容\n", "test: initial");

        GitRepositoryOperationResult cloned = await service.CloneAsync(source, destination);

        Assert.IsTrue(cloned.IsSuccess, cloned.ErrorMessage);
        Assert.AreEqual(GitRepositoryKind.WorkingTree, cloned.Repository!.Kind);
        Assert.AreEqual(Path.GetFullPath(destination), cloned.Repository.RepositoryRoot);
        string clonedContent = await File.ReadAllTextAsync(Path.Combine(destination, "readme.txt"));
        Assert.AreEqual("内容\n", clonedContent.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task 合并冲突和进行中操作可被识别()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        GitRepositoryService service = new(runtime);
        GitRepositoryOperationResult initialized = await service.InitializeAsync(temporary.FullPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "conflict.txt", "base\n", "test: base");
        GitCommandResult branchResult = await GitTestEnvironment.RunAsync(
            runtime,
            temporary.FullPath,
            "symbolic-ref",
            "--short",
            "HEAD");
        string originalBranch = branchResult.StandardOutput.Trim();
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "checkout", "-b", "other");
        await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "conflict.txt", "other\n", "test: other");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "checkout", originalBranch);
        await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "conflict.txt", "main\n", "test: main");
        GitCommandResult mergeResult = await new GitCommandRunner().RunAsync(
            runtime.ExecutablePath!,
            temporary.FullPath,
            ["merge", "other"],
            GitCommandMode.LocalWrite);
        Assert.IsFalse(mergeResult.IsSuccess);

        GitRepositoryOperationResult state = await service.InspectAsync(temporary.FullPath);

        Assert.IsTrue(state.IsSuccess, state.ErrorMessage);
        Assert.IsTrue(state.Repository!.HasConflicts);
        Assert.AreEqual(GitOperationKind.Merge, state.Repository.Operation);
    }
}
