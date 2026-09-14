using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Terminal;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitWorktreeServiceTests
{
    [TestMethod]
    public async Task Worktree创建列出并只在干净时安全移除()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string mainPath = temporary.GetPath("main");
        string linkedPath = temporary.GetPath("linked");
        Directory.CreateDirectory(mainPath);
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime).InitializeAsync(mainPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, mainPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(runtime, mainPath, "config", "user.email", "augit-tests@example.invalid");
        await GitTestEnvironment.CommitFileAsync(runtime, mainPath, "base.txt", "base\n", "test: base");
        await GitTestEnvironment.RunAsync(runtime, mainPath, "branch", "feature");
        GitWorktreeService service = new(runtime);

        GitActionResult created = await service.CreateAsync(
            initialized.Repository!,
            linkedPath,
            "feature");
        GitWorktreeListResult listed = await service.ReadAsync(initialized.Repository!);
        await File.WriteAllTextAsync(Path.Combine(linkedPath, "dirty.txt"), "dirty\n");
        GitActionResult dirtyRemoval = await service.RemoveAsync(initialized.Repository!, linkedPath);
        bool existedAfterDirtyRemoval = Directory.Exists(linkedPath);
        File.Delete(Path.Combine(linkedPath, "dirty.txt"));
        GitActionResult cleanRemoval = await service.RemoveAsync(initialized.Repository!, linkedPath);
        GitWorktreeListResult finalList = await service.ReadAsync(initialized.Repository!);

        Assert.IsTrue(created.IsSuccess, created.ErrorMessage);
        Assert.HasCount(2, listed.Worktrees!);
        GitWorktreeInfo linked = listed.Worktrees!.Single(worktree => !worktree.IsCurrent);
        Assert.AreEqual("feature", linked.Branch);
        Assert.IsFalse(dirtyRemoval.IsSuccess);
        Assert.AreEqual(GitOperationFailureKind.InvalidRequest, dirtyRemoval.FailureKind);
        Assert.IsTrue(existedAfterDirtyRemoval);
        Assert.IsTrue(cleanRemoval.IsSuccess, cleanRemoval.ErrorMessage);
        Assert.IsFalse(Directory.Exists(linkedPath));
        Assert.HasCount(1, finalList.Worktrees!);
        Assert.IsTrue(finalList.Worktrees![0].IsCurrent);
    }

    [TestMethod]
    public async Task 当前Worktree不能从自身窗口移除()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(temporary.FullPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "user.email", "augit-tests@example.invalid");
        await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "base.txt", "base\n", "test: base");

        GitActionResult result = await new GitWorktreeService(runtime)
            .RemoveAsync(initialized.Repository!, temporary.FullPath);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(GitOperationFailureKind.InvalidRequest, result.FailureKind);
        Assert.IsTrue(Directory.Exists(temporary.FullPath));
    }

    [TestMethod]
    public async Task 运行中的Augit内置终端阻止Worktree移除()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string mainPath = temporary.GetPath("main");
        string linkedPath = temporary.GetPath("linked");
        Directory.CreateDirectory(mainPath);
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime).InitializeAsync(mainPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, mainPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(runtime, mainPath, "config", "user.email", "augit-tests@example.invalid");
        await GitTestEnvironment.CommitFileAsync(runtime, mainPath, "base.txt", "base\n", "test: base");
        await GitTestEnvironment.RunAsync(runtime, mainPath, "branch", "feature");
        TerminalSessionRegistry registry = new(temporary.GetPath("locks"));
        GitWorktreeService service = new(
            runtime,
            new GitCommandRunner(TimeSpan.FromSeconds(30), 1024 * 1024),
            new GitStatusService(runtime),
            registry);
        GitActionResult created = await service.CreateAsync(initialized.Repository!, linkedPath, "feature");
        Assert.IsTrue(created.IsSuccess, created.ErrorMessage);

        using TerminalSessionLease? session = registry.TryAcquire(linkedPath);
        Assert.IsNotNull(session);
        GitActionResult blocked = await service.RemoveAsync(initialized.Repository!, linkedPath);

        Assert.IsFalse(blocked.IsSuccess);
        StringAssert.Contains(blocked.ErrorMessage, "内置终端");
        Assert.IsTrue(Directory.Exists(linkedPath));
        session.Dispose();
        GitActionResult removed = await service.RemoveAsync(initialized.Repository!, linkedPath);
        Assert.IsTrue(removed.IsSuccess, removed.ErrorMessage);
    }

    [TestMethod]
    public async Task Worktree移除就绪状态反映干净脏状态和终端占用()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string mainPath = temporary.GetPath("main");
        string linkedPath = temporary.GetPath("linked");
        Directory.CreateDirectory(mainPath);
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime).InitializeAsync(mainPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, mainPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(runtime, mainPath, "config", "user.email", "augit-tests@example.invalid");
        await GitTestEnvironment.CommitFileAsync(runtime, mainPath, "base.txt", "base\n", "test: base");
        await GitTestEnvironment.RunAsync(runtime, mainPath, "branch", "feature");
        TerminalSessionRegistry registry = new(temporary.GetPath("locks"));
        GitWorktreeService service = new(
            runtime,
            new GitCommandRunner(TimeSpan.FromSeconds(30), 1024 * 1024),
            new GitStatusService(runtime),
            registry);
        GitActionResult created = await service.CreateAsync(initialized.Repository!, linkedPath, "feature");
        Assert.IsTrue(created.IsSuccess, created.ErrorMessage);

        GitWorktreeRemovalReadinessResult clean = await service.InspectRemovalReadinessAsync(
            initialized.Repository!,
            linkedPath);
        await File.WriteAllTextAsync(Path.Combine(linkedPath, "dirty.txt"), "dirty\n");
        GitWorktreeRemovalReadinessResult dirty = await service.InspectRemovalReadinessAsync(
            initialized.Repository!,
            linkedPath);
        File.Delete(Path.Combine(linkedPath, "dirty.txt"));
        using TerminalSessionLease? lease = registry.TryAcquire(linkedPath);
        Assert.IsNotNull(lease);
        GitWorktreeRemovalReadinessResult activeTerminal = await service.InspectRemovalReadinessAsync(
            initialized.Repository!,
            linkedPath);

        Assert.IsTrue(clean.IsSuccess, clean.ErrorMessage);
        Assert.IsTrue(clean.Readiness!.CanRemove);
        Assert.IsTrue(clean.Readiness.IsClean);
        Assert.IsFalse(clean.Readiness.HasActiveTerminal);
        Assert.IsFalse(dirty.Readiness!.CanRemove);
        Assert.IsFalse(dirty.Readiness.IsClean);
        StringAssert.Contains(dirty.Readiness.Reason, "本地改动");
        Assert.IsFalse(activeTerminal.Readiness!.CanRemove);
        Assert.IsTrue(activeTerminal.Readiness.HasActiveTerminal);
        StringAssert.Contains(activeTerminal.Readiness.Reason, "内置终端");
    }
}
