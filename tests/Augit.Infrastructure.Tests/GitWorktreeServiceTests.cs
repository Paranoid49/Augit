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
        // 权威 `GitWorktreeListParser` 用"`git worktree list` 的第一项"判定主工作树（`isFirst`）。
        Assert.IsTrue(listed.Worktrees![0].IsMain);
        Assert.IsTrue(listed.Worktrees![0].IsCurrent);
        GitWorktreeInfo linked = listed.Worktrees!.Single(worktree => !worktree.IsCurrent);
        Assert.IsFalse(linked.IsMain);
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
    public async Task 主工作树不能从链接Worktree窗口移除()
    {
        // 权威 `RemoveWorkingTreeAction.isEnabledFor()`：`!it.isCurrent && !it.isMain`。
        // 真实场景是"当前窗口打开的是链接 Worktree"，此时主工作树既不 current、也不可移除；
        // 若不显式拦截，用户点下去拿到的是 `git worktree remove` 的原始报错。
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string mainPath = temporary.GetPath("main");
        string linkedPath = temporary.GetPath("linked");
        Directory.CreateDirectory(mainPath);
        GitRepositoryService repositories = new(runtime);
        GitRepositoryOperationResult mainRepository = await repositories.InitializeAsync(mainPath);
        Assert.IsTrue(mainRepository.IsSuccess, mainRepository.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, mainPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(runtime, mainPath, "config", "user.email", "augit-tests@example.invalid");
        await GitTestEnvironment.CommitFileAsync(runtime, mainPath, "base.txt", "base\n", "test: base");
        await GitTestEnvironment.RunAsync(runtime, mainPath, "branch", "feature");
        GitWorktreeService service = new(runtime);
        GitActionResult created = await service.CreateAsync(mainRepository.Repository!, linkedPath, "feature");
        Assert.IsTrue(created.IsSuccess, created.ErrorMessage);

        GitRepositoryOperationResult linkedRepository = await repositories.InspectAsync(linkedPath);
        Assert.IsTrue(linkedRepository.IsSuccess, linkedRepository.ErrorMessage);
        GitWorktreeListResult listed = await service.ReadAsync(linkedRepository.Repository!);
        GitWorktreeInfo main = listed.Worktrees!.Single(worktree => worktree.IsMain);
        Assert.IsFalse(main.IsCurrent, "从链接 Worktree 观察时主工作树不应是当前工作树。");

        GitWorktreeRemovalReadinessResult readiness = await service.InspectRemovalReadinessAsync(
            linkedRepository.Repository!,
            mainPath);
        GitActionResult refused = await service.RemoveAsync(linkedRepository.Repository!, mainPath);

        Assert.IsTrue(readiness.IsSuccess, readiness.ErrorMessage);
        Assert.IsFalse(readiness.Readiness!.CanRemove);
        StringAssert.Contains(readiness.Readiness.Reason, "主工作树");
        Assert.IsFalse(refused.IsSuccess);
        Assert.AreEqual(GitOperationFailureKind.InvalidRequest, refused.FailureKind);
        StringAssert.Contains(refused.ErrorMessage, "主工作树");
        Assert.IsTrue(Directory.Exists(mainPath));
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
