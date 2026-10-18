using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitRepositoryServiceTests
{
    [TestMethod]
    public async Task 仓库探测保留独立Git目录和子目录工作区()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string root = temporary.GetPath("工作区 空格"), metadata = temporary.GetPath("独立 元数据");
        Directory.CreateDirectory(root);
        Assert.IsTrue((await GitTestEnvironment.RunAsync(runtime, root, "init", "--separate-git-dir", metadata)).IsSuccess);
        string nested = Path.Combine(root, "子目录");
        Directory.CreateDirectory(nested);
        GitRepositoryOperationResult result = await new GitRepositoryService(runtime).InspectAsync(nested);
        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(Path.GetFullPath(root), result.Repository!.RepositoryRoot);
        Assert.AreEqual(Path.GetFullPath(nested), result.Repository.WorkspacePath);
        Assert.AreEqual(Path.GetFullPath(metadata), result.Repository.GitDirectory);
        Assert.AreEqual(result.Repository.GitDirectory, result.Repository.GitCommonDirectory);
    }

    [TestMethod]
    public async Task 并行探测保留裸仓库目录且不要求工作区根目录()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "init", "--bare");
        GitRepositoryOperationResult result = await new GitRepositoryService(runtime).InspectAsync(temporary.FullPath);
        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(GitRepositoryKind.BareRepository, result.Repository!.Kind);
        Assert.IsNull(result.Repository.RepositoryRoot);
        Assert.AreEqual(Path.GetFullPath(temporary.FullPath), result.Repository.GitDirectory);
        Assert.AreEqual(result.Repository.GitDirectory, result.Repository.GitCommonDirectory);
    }

    [TestMethod]
    public async Task 并行探测保留链接工作区独立Git目录与公共目录()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string primary = temporary.GetPath("primary"), linked = temporary.GetPath("linked");
        Directory.CreateDirectory(primary);
        GitRepositoryService service = new(runtime);
        Assert.IsTrue((await service.InitializeAsync(primary)).IsSuccess);
        await GitTestEnvironment.CommitFileAsync(runtime, primary, "readme.txt", "内容\n", "test: initial");
        await GitTestEnvironment.RunAsync(runtime, primary, "worktree", "add", "-b", "linked", linked);
        GitRepositoryOperationResult result = await service.InspectAsync(linked);
        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(Path.GetFullPath(linked), result.Repository!.RepositoryRoot);
        Assert.AreNotEqual(result.Repository.GitDirectory, result.Repository.GitCommonDirectory);
        Assert.AreEqual(Path.Combine(primary, ".git"), result.Repository.GitCommonDirectory);
        Assert.IsFalse(result.Repository.HasConflicts);
    }

    [TestMethod]
    public async Task 已取消的仓库检查不返回成功快照()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        GitRepositoryOperationResult result = await new GitRepositoryService(runtime).InspectAsync(temporary.FullPath, cancellation.Token);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(GitOperationFailureKind.Cancelled, result.FailureKind);
    }

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
    public async Task 本地仓库可通过文件协议浅克隆最近一次提交()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string source = temporary.GetPath("source");
        string destination = temporary.GetPath("shallow");
        Directory.CreateDirectory(source);
        GitRepositoryService service = new(runtime);
        GitRepositoryOperationResult initialized = await service.InitializeAsync(source);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.CommitFileAsync(runtime, source, "history.txt", "一\n", "test: first");
        await GitTestEnvironment.CommitFileAsync(runtime, source, "history.txt", "二\n", "test: second");
        await GitTestEnvironment.CommitFileAsync(runtime, source, "history.txt", "三\n", "test: third");

        string sourceUri = new Uri(Path.GetFullPath(source) + Path.DirectorySeparatorChar).AbsoluteUri;
        GitRepositoryOperationResult cloned = await service.CloneAsync(sourceUri, destination, depth: 1);

        Assert.IsTrue(cloned.IsSuccess, cloned.ErrorMessage);
        GitCommandResult count = await GitTestEnvironment.RunAsync(runtime, destination, "rev-list", "--count", "HEAD");
        Assert.AreEqual("1", count.StandardOutput.Trim());
        Assert.AreEqual("三\n", (await File.ReadAllTextAsync(Path.Combine(destination, "history.txt"))).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task 非法浅克隆深度不会创建目标目录()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string destination = temporary.GetPath("invalid-depth");
        GitRepositoryService service = new(runtime);

        GitRepositoryOperationResult result = await service.CloneAsync("https://example.invalid/repository.git", destination, depth: 0);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(GitOperationFailureKind.InvalidRequest, result.FailureKind);
        Assert.AreEqual("浅克隆深度必须是正整数。", result.ErrorMessage);
        Assert.IsFalse(Directory.Exists(destination));
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
