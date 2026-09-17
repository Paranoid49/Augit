using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitOperationServiceTests
{
    [TestMethod]
    [DataRow(GitAdvancedOperationKind.Merge)]
    [DataRow(GitAdvancedOperationKind.Rebase)]
    [DataRow(GitAdvancedOperationKind.CherryPick)]
    [DataRow(GitAdvancedOperationKind.Revert)]
    public async Task 四种操作无冲突时由本机Git完成并刷新实际状态(GitAdvancedOperationKind kind)
    {
        OperationRepository setup = await CreateSuccessRepositoryAsync(kind);
        using (setup.Temporary)
        {
            GitOperationService service = new(setup.Runtime);

            GitAdvancedOperationResult result = await service.StartAsync(
                setup.Repository,
                new(kind, setup.TargetRevision));

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.IsFalse(result.Session!.IsInProgress);
            Assert.IsFalse(result.Session.HasConflicts);
            Assert.IsNotNull(result.ActualStatus);
        }
    }

    [TestMethod]
    [DataRow(GitAdvancedOperationKind.Merge, GitOperationAction.Continue)]
    [DataRow(GitAdvancedOperationKind.Merge, GitOperationAction.Abort)]
    [DataRow(GitAdvancedOperationKind.Rebase, GitOperationAction.Continue)]
    [DataRow(GitAdvancedOperationKind.Rebase, GitOperationAction.Skip)]
    [DataRow(GitAdvancedOperationKind.Rebase, GitOperationAction.Abort)]
    [DataRow(GitAdvancedOperationKind.CherryPick, GitOperationAction.Continue)]
    [DataRow(GitAdvancedOperationKind.CherryPick, GitOperationAction.Skip)]
    [DataRow(GitAdvancedOperationKind.CherryPick, GitOperationAction.Abort)]
    [DataRow(GitAdvancedOperationKind.Revert, GitOperationAction.Continue)]
    [DataRow(GitAdvancedOperationKind.Revert, GitOperationAction.Skip)]
    [DataRow(GitAdvancedOperationKind.Revert, GitOperationAction.Abort)]
    public async Task 冲突会话只开放Git实际支持的动作并可完成(
        GitAdvancedOperationKind kind,
        GitOperationAction action)
    {
        OperationRepository setup = await CreateConflictRepositoryAsync(kind);
        using (setup.Temporary)
        {
            GitOperationService service = new(setup.Runtime);
            GitConflictService conflictService = new(setup.Runtime, service);
            GitAdvancedOperationResult started = await service.StartAsync(
                setup.Repository,
                new(kind, setup.TargetRevision));

            Assert.IsFalse(started.IsSuccess);
            Assert.IsTrue(started.Session!.HasConflicts);
            Assert.IsTrue(started.Session.CanAbort);
            Assert.AreEqual(kind != GitAdvancedOperationKind.Merge, started.Session.CanSkip);
            Assert.IsFalse(started.Session.CanContinue);
            // "支持但前置未满足"与"当前操作根本不支持"必须可区分：界面据此决定
            // 保留并禁用（附原因）还是直接不显示（规格 §7.13/§10.3）。
            Assert.IsTrue(started.Session.SupportsContinue);
            Assert.HasCount(1, started.Session.ConflictFiles);
            if (kind == GitAdvancedOperationKind.Rebase)
            {
                Assert.AreEqual(1, started.Session.CurrentStep);
                Assert.AreEqual(1, started.Session.TotalSteps);
            }
            else
            {
                Assert.IsNull(started.Session.CurrentStep);
                Assert.IsNull(started.Session.TotalSteps);
            }

            if (action == GitOperationAction.Continue)
            {
                GitConflictMutationResult resolved = await conflictService.AcceptSideAsync(
                    setup.Repository,
                    "conflict.txt",
                    GitConflictSide.Theirs);
                Assert.IsTrue(resolved.IsSuccess, resolved.ErrorMessage);
                Assert.IsTrue(resolved.Session!.CanContinue);
            }

            GitAdvancedOperationResult completed = await service.ExecuteActionAsync(setup.Repository, action);

            Assert.IsTrue(completed.IsSuccess, completed.ErrorMessage);
            Assert.IsFalse(completed.Session!.IsInProgress);
            Assert.IsFalse(completed.Session.HasConflicts);
        }
    }

    [TestMethod]
    public async Task Merge冲突时拒绝Skip且仓库保持进行中()
    {
        OperationRepository setup = await CreateConflictRepositoryAsync(GitAdvancedOperationKind.Merge);
        using (setup.Temporary)
        {
            GitOperationService service = new(setup.Runtime);
            _ = await service.StartAsync(
                setup.Repository,
                new(GitAdvancedOperationKind.Merge, setup.TargetRevision));

            GitAdvancedOperationResult skipped = await service.ExecuteActionAsync(
                setup.Repository,
                GitOperationAction.Skip);

            Assert.IsFalse(skipped.IsSuccess);
            Assert.AreEqual(GitOperationFailureKind.InvalidRequest, skipped.FailureKind);
            Assert.AreEqual(GitOperationKind.Merge, skipped.Session!.Kind);
            Assert.IsTrue(skipped.Session.HasConflicts);
        }
    }

    [TestMethod]
    public async Task SmartCheckout可在切换后恢复已跟踪和未跟踪改动并删除临时Stash()
    {
        OperationRepository setup = await CreateSmartCheckoutRepositoryAsync(conflicting: false);
        using (setup.Temporary)
        {
            GitOperationService service = new(setup.Runtime);

            GitAdvancedOperationResult result = await service.SmartCheckoutAsync(
                setup.Repository,
                "target");
            string stashList = (await GitTestEnvironment.RunAsync(
                setup.Runtime,
                setup.Temporary.FullPath,
                "stash",
                "list")).StandardOutput;

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.AreEqual("target", result.ActualStatus!.CurrentBranch);
            Assert.AreEqual(
                "本地修改\n",
                (await File.ReadAllTextAsync(setup.Temporary.GetPath("shared.txt")))
                    .Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.AreEqual(
                "未跟踪\n",
                (await File.ReadAllTextAsync(setup.Temporary.GetPath("untracked.txt")))
                    .Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.AreEqual(string.Empty, stashList);
        }
    }

    [TestMethod]
    public async Task SmartCheckout恢复冲突进入解决流程并在完成后删除临时Stash()
    {
        OperationRepository setup = await CreateSmartCheckoutRepositoryAsync(conflicting: true);
        using (setup.Temporary)
        {
            GitOperationService service = new(setup.Runtime);
            GitConflictService conflictService = new(setup.Runtime, service);

            GitAdvancedOperationResult started = await service.SmartCheckoutAsync(
                setup.Repository,
                "target");

            Assert.IsFalse(started.IsSuccess);
            Assert.AreEqual(GitOperationKind.SmartCheckout, started.Session!.Kind);
            Assert.IsTrue(started.Session.HasConflicts);
            Assert.IsFalse(started.Session.CanAbort);
            Assert.IsTrue(started.Session.SupportsContinue);

            service = new(setup.Runtime);
            conflictService = new(setup.Runtime, service);
            GitAdvancedOperationResult restoredSession = await service.InspectAsync(setup.Repository);
            Assert.AreEqual(GitOperationKind.SmartCheckout, restoredSession.Session!.Kind);
            Assert.IsTrue(restoredSession.Session.HasConflicts);

            GitConflictMutationResult resolved = await conflictService.AcceptSideAsync(
                setup.Repository,
                "shared.txt",
                GitConflictSide.Theirs);
            GitAdvancedOperationResult completed = await service.ExecuteActionAsync(
                setup.Repository,
                GitOperationAction.Continue);
            string stashList = (await GitTestEnvironment.RunAsync(
                setup.Runtime,
                setup.Temporary.FullPath,
                "stash",
                "list")).StandardOutput;

            Assert.IsTrue(resolved.IsSuccess, resolved.ErrorMessage);
            Assert.IsTrue(completed.IsSuccess, completed.ErrorMessage);
            Assert.AreEqual("target", completed.ActualStatus!.CurrentBranch);
            Assert.AreEqual(string.Empty, stashList);
            Assert.Contains("本地修改", await File.ReadAllTextAsync(setup.Temporary.GetPath("shared.txt")));
        }
    }

    [TestMethod]
    [DataRow(GitAdvancedOperationKind.CherryPick)]
    [DataRow(GitAdvancedOperationKind.Revert)]
    public async Task 空的CherryPick和Revert只允许Skip(GitAdvancedOperationKind kind)
    {
        OperationRepository setup = await CreateConflictRepositoryAsync(kind);
        using (setup.Temporary)
        {
            GitOperationService service = new(setup.Runtime);
            GitConflictService conflictService = new(setup.Runtime, service);
            _ = await service.StartAsync(setup.Repository, new(kind, setup.TargetRevision));

            GitConflictMutationResult resolved = await conflictService.AcceptSideAsync(
                setup.Repository,
                "conflict.txt",
                GitConflictSide.Yours);

            Assert.IsTrue(resolved.IsSuccess, resolved.ErrorMessage);
            Assert.IsFalse(resolved.Session!.CanContinue);
            Assert.IsTrue(resolved.Session.CanSkip);
        }
    }

    private static async Task<OperationRepository> CreateSuccessRepositoryAsync(GitAdvancedOperationKind kind)
    {
        OperationRepository setup = await CreateRepositoryAsync();
        await GitTestEnvironment.CommitFileAsync(
            setup.Runtime,
            setup.Temporary.FullPath,
            "base.txt",
            "base\n",
            "test: base");
        string originalBranch = await CurrentBranchAsync(setup);
        if (kind == GitAdvancedOperationKind.Revert)
        {
            await GitTestEnvironment.CommitFileAsync(
                setup.Runtime,
                setup.Temporary.FullPath,
                "revert.txt",
                "value\n",
                "test: reversible");
            string target = await HeadAsync(setup);
            return setup with { OriginalBranch = originalBranch, TargetRevision = target };
        }

        await GitTestEnvironment.RunAsync(setup.Runtime, setup.Temporary.FullPath, "checkout", "-b", "target");
        await GitTestEnvironment.CommitFileAsync(
            setup.Runtime,
            setup.Temporary.FullPath,
            "target.txt",
            "target\n",
            "test: target");
        string targetRevision = kind is GitAdvancedOperationKind.Merge or GitAdvancedOperationKind.Rebase
            ? "target"
            : await HeadAsync(setup);
        await GitTestEnvironment.RunAsync(setup.Runtime, setup.Temporary.FullPath, "checkout", originalBranch);
        return setup with { OriginalBranch = originalBranch, TargetRevision = targetRevision };
    }

    private static async Task<OperationRepository> CreateConflictRepositoryAsync(GitAdvancedOperationKind kind)
    {
        OperationRepository setup = await CreateRepositoryAsync();
        await GitTestEnvironment.CommitFileAsync(
            setup.Runtime,
            setup.Temporary.FullPath,
            "conflict.txt",
            "base\n",
            "test: base");
        string originalBranch = await CurrentBranchAsync(setup);
        if (kind == GitAdvancedOperationKind.Revert)
        {
            await GitTestEnvironment.CommitFileAsync(
                setup.Runtime,
                setup.Temporary.FullPath,
                "conflict.txt",
                "to revert\n",
                "test: reversible");
            string target = await HeadAsync(setup);
            await GitTestEnvironment.CommitFileAsync(
                setup.Runtime,
                setup.Temporary.FullPath,
                "conflict.txt",
                "current\n",
                "test: current");
            return setup with { OriginalBranch = originalBranch, TargetRevision = target };
        }

        await GitTestEnvironment.RunAsync(setup.Runtime, setup.Temporary.FullPath, "checkout", "-b", "target");
        await GitTestEnvironment.CommitFileAsync(
            setup.Runtime,
            setup.Temporary.FullPath,
            "conflict.txt",
            "target\n",
            "test: target");
        string targetCommit = await HeadAsync(setup);
        await GitTestEnvironment.RunAsync(setup.Runtime, setup.Temporary.FullPath, "checkout", originalBranch);
        await GitTestEnvironment.CommitFileAsync(
            setup.Runtime,
            setup.Temporary.FullPath,
            "conflict.txt",
            "current\n",
            "test: current");
        string targetRevision = kind is GitAdvancedOperationKind.Merge or GitAdvancedOperationKind.Rebase
            ? "target"
            : targetCommit;
        return setup with { OriginalBranch = originalBranch, TargetRevision = targetRevision };
    }

    private static async Task<OperationRepository> CreateSmartCheckoutRepositoryAsync(bool conflicting)
    {
        OperationRepository setup = await CreateRepositoryAsync();
        await GitTestEnvironment.CommitFileAsync(
            setup.Runtime,
            setup.Temporary.FullPath,
            "shared.txt",
            "base\n",
            "test: base");
        string originalBranch = await CurrentBranchAsync(setup);
        await GitTestEnvironment.RunAsync(setup.Runtime, setup.Temporary.FullPath, "checkout", "-b", "target");
        if (conflicting)
        {
            await GitTestEnvironment.CommitFileAsync(
                setup.Runtime,
                setup.Temporary.FullPath,
                "shared.txt",
                "目标分支\n",
                "test: target");
        }
        else
        {
            await GitTestEnvironment.CommitFileAsync(
                setup.Runtime,
                setup.Temporary.FullPath,
                "target.txt",
                "target\n",
                "test: target");
        }

        await GitTestEnvironment.RunAsync(setup.Runtime, setup.Temporary.FullPath, "checkout", originalBranch);
        await File.WriteAllTextAsync(setup.Temporary.GetPath("shared.txt"), "本地修改\n");
        await File.WriteAllTextAsync(setup.Temporary.GetPath("untracked.txt"), "未跟踪\n");
        return setup with { OriginalBranch = originalBranch, TargetRevision = "target" };
    }

    private static async Task<OperationRepository> CreateRepositoryAsync()
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
        return new(temporary, runtime, initialized.Repository!, string.Empty, string.Empty);
    }

    private static async Task<string> CurrentBranchAsync(OperationRepository setup)
    {
        return (await GitTestEnvironment.RunAsync(
            setup.Runtime,
            setup.Temporary.FullPath,
            "branch",
            "--show-current")).StandardOutput.Trim();
    }

    private static async Task<string> HeadAsync(OperationRepository setup)
    {
        return (await GitTestEnvironment.RunAsync(
            setup.Runtime,
            setup.Temporary.FullPath,
            "rev-parse",
            "HEAD")).StandardOutput.Trim();
    }

    private sealed record OperationRepository(
        TemporaryDirectory Temporary,
        GitRuntimeInfo Runtime,
        GitRepositorySnapshot Repository,
        string OriginalBranch,
        string TargetRevision);
}
