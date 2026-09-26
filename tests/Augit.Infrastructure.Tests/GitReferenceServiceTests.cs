using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitReferenceServiceTests
{
    [TestMethod]
    public async Task 分支创建切换重命名删除和覆盖保护均使用本机Git()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "shared.txt", "base\n", "test: base");
            string main = (await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "branch", "--show-current"))
                .StandardOutput.Trim();
            GitReferenceService service = new(runtime);

            GitActionResult created = await service.CreateBranchAsync(repository, "feature");
            GitActionResult switched = await service.SwitchBranchAsync(repository, "feature");
            await File.WriteAllTextAsync(temporary.GetPath("shared.txt"), "feature\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "shared.txt");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "commit", "-m", "feat: feature value");
            await service.SwitchBranchAsync(repository, main);
            await File.WriteAllTextAsync(temporary.GetPath("shared.txt"), "local change\n");
            GitActionResult blocked = await service.SwitchBranchAsync(repository, "feature");
            string preserved = await File.ReadAllTextAsync(temporary.GetPath("shared.txt"));
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "restore", "--", "shared.txt");
            GitActionResult renamed = await service.RenameBranchAsync(repository, "feature", "feature-renamed");
            GitActionResult deletedSafely = await service.DeleteBranchAsync(repository, "feature-renamed", force: false);
            GitActionResult deletedForce = await service.DeleteBranchAsync(repository, "feature-renamed", force: true);
            GitReferenceResult references = await service.ReadAsync(repository);

            Assert.IsTrue(created.IsSuccess, created.ErrorMessage);
            Assert.IsTrue(switched.IsSuccess, switched.ErrorMessage);
            Assert.IsFalse(blocked.IsSuccess);
            Assert.Contains("local change", preserved, StringComparison.Ordinal);
            Assert.IsTrue(renamed.IsSuccess, renamed.ErrorMessage);
            Assert.IsFalse(deletedSafely.IsSuccess);
            Assert.IsTrue(deletedForce.IsSuccess, deletedForce.ErrorMessage);
            Assert.IsFalse(references.Snapshot!.Branches.Any(branch => branch.Name == "feature-renamed"));
        }
    }

    [TestMethod]
    public async Task 远程跟踪分支和本地跟踪关系可创建设置与取消()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "base.txt", "base\n", "test: base");
            string main = (await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "branch", "--show-current"))
                .StandardOutput.Trim();
            string bare = temporary.GetPath("remote.git");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "init", "--bare", bare);
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "remote", "add", "origin", bare);
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "push", "origin", main);
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "fetch", "origin");
            GitReferenceService service = new(runtime);

            GitActionResult trackingCreated = await service.CreateTrackingBranchAsync(
                repository,
                "tracking",
                $"origin/{main}");
            GitReferenceResult createdReferences = await service.ReadAsync(repository);
            GitActionResult unset = await service.UnsetTrackingAsync(repository, "tracking");
            GitActionResult set = await service.SetTrackingAsync(repository, "tracking", $"origin/{main}");
            GitReferenceResult finalReferences = await service.ReadAsync(repository);

            Assert.IsTrue(trackingCreated.IsSuccess, trackingCreated.ErrorMessage);
            GitBranchInfo created = createdReferences.Snapshot!.Branches.Single(branch => branch.Name == "tracking");
            Assert.IsTrue(created.IsCurrent);
            Assert.AreEqual($"origin/{main}", created.Upstream);
            Assert.IsTrue(unset.IsSuccess, unset.ErrorMessage);
            Assert.IsTrue(set.IsSuccess, set.ErrorMessage);
            Assert.AreEqual(
                $"origin/{main}",
                finalReferences.Snapshot!.Branches.Single(branch => branch.Name == "tracking").Upstream);
        }
    }

    [TestMethod]
    public async Task 轻量和说明标签可创建推送删除并正确列出()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "base.txt", "base\n", "test: base");
            string bare = temporary.GetPath("remote.git");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "init", "--bare", bare);
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "remote", "add", "origin", bare);
            GitReferenceService service = new(runtime);

            GitActionResult lightweight = await service.CreateTagAsync(repository, "v1", "HEAD", null);
            GitActionResult annotated = await service.CreateTagAsync(repository, "v2", "HEAD", "版本二");
            GitReferenceResult references = await service.ReadAsync(repository);
            GitActionResult pushed = await service.PushTagAsync(repository, "origin", "v2");
            GitCommandResult remoteTag = await GitTestEnvironment.RunAsync(
                runtime,
                temporary.FullPath,
                "ls-remote",
                "--tags",
                "origin",
                "refs/tags/v2");
            GitActionResult remoteDeleted = await service.DeleteRemoteTagAsync(repository, "origin", "v2");
            GitActionResult localDeleted = await service.DeleteLocalTagAsync(repository, "v1");

            Assert.IsTrue(lightweight.IsSuccess, lightweight.ErrorMessage);
            Assert.IsTrue(annotated.IsSuccess, annotated.ErrorMessage);
            Assert.IsFalse(references.Snapshot!.Tags.Single(tag => tag.Name == "v1").IsAnnotated);
            GitTagInfo annotatedTag = references.Snapshot.Tags.Single(tag => tag.Name == "v2");
            Assert.IsTrue(annotatedTag.IsAnnotated);
            Assert.AreEqual("版本二", annotatedTag.Message);
            Assert.IsTrue(pushed.IsSuccess, pushed.ErrorMessage);
            Assert.IsFalse(string.IsNullOrWhiteSpace(remoteTag.StandardOutput));
            Assert.IsTrue(remoteDeleted.IsSuccess, remoteDeleted.ErrorMessage);
            Assert.IsTrue(localDeleted.IsSuccess, localDeleted.ErrorMessage);
        }
    }

    [TestMethod]
    public async Task 标签或提交可检出为分离Head且非法引用不改变仓库()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "base.txt", "base\n", "test: base");
            GitReferenceService service = new(runtime);
            GitActionResult tagCreated = await service.CreateTagAsync(repository, "v1", "HEAD", null);
            GitActionResult checkedOut = await service.CheckoutReferenceAsync(repository, "v1");
            string detached = (await GitTestEnvironment.RunAsync(
                runtime,
                temporary.FullPath,
                "branch",
                "--show-current")).StandardOutput.Trim();
            string beforeInvalid = (await GitTestEnvironment.RunAsync(
                runtime,
                temporary.FullPath,
                "rev-parse",
                "HEAD")).StandardOutput.Trim();
            GitActionResult invalid = await service.CheckoutReferenceAsync(repository, "不存在的版本");
            string afterInvalid = (await GitTestEnvironment.RunAsync(
                runtime,
                temporary.FullPath,
                "rev-parse",
                "HEAD")).StandardOutput.Trim();

            Assert.IsTrue(tagCreated.IsSuccess, tagCreated.ErrorMessage);
            Assert.IsTrue(checkedOut.IsSuccess, checkedOut.ErrorMessage);
            Assert.AreEqual(string.Empty, detached);
            Assert.IsFalse(invalid.IsSuccess);
            Assert.AreEqual(beforeInvalid, afterInvalid);
        }
    }

    [TestMethod]
    public async Task 非法分支标签和引用被拒绝且不改变仓库状态()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "base.txt", "base\n", "test: base");
            GitReferenceService service = new(runtime);
            string before = (await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "branch", "--show-current"))
                .StandardOutput.Trim();

            GitActionResult invalidBranch = await service.CreateBranchAsync(repository, "--force");
            GitActionResult invalidSwitch = await service.SwitchBranchAsync(repository, "不存在的分支");
            GitActionResult invalidTag = await service.CreateTagAsync(repository, "bad\nname", "HEAD", null);
            GitReferenceResult references = await service.ReadAsync(repository);
            string after = (await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "branch", "--show-current"))
                .StandardOutput.Trim();

            Assert.IsFalse(invalidBranch.IsSuccess);
            Assert.AreEqual(GitOperationFailureKind.InvalidRequest, invalidBranch.FailureKind);
            Assert.IsFalse(invalidSwitch.IsSuccess);
            Assert.AreEqual(GitOperationFailureKind.InvalidRequest, invalidSwitch.FailureKind);
            Assert.IsFalse(invalidTag.IsSuccess);
            Assert.AreEqual(GitOperationFailureKind.InvalidRequest, invalidTag.FailureKind);
            Assert.AreEqual(before, after);
            Assert.IsFalse(references.Snapshot!.Branches.Any(branch => branch.Name == "--force"));
            Assert.IsEmpty(references.Snapshot.Tags);
        }
    }

    [TestMethod]
    public async Task 我的分支要求独占提交非空且全部由当前用户提交()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            // 基线提交（作者 = 我）。三条分支都从这里出发，决定"独占提交"归属。
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "base.txt", "base\n", "test: base");
            GitReferenceResult initial = await new GitReferenceService(runtime).ReadAsync(repository);
            Assert.IsTrue(initial.IsSuccess, initial.ErrorMessage);
            string defaultBranch = initial.Snapshot!.Branches.Single(branch => branch.IsCurrent).Name;

            // their 提交：作者是别人，制造"不完全属于我"的独占提交。
            // 注意不能用 `CommitFileAsync`：它固定带 `-c user.email=augit-tests@…`，会把作者强行改回我。
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-c", "their");
            await File.WriteAllTextAsync(Path.Combine(temporary.FullPath, "their.txt"), "their\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "their.txt");
            await GitTestEnvironment.RunAsync(
                runtime,
                temporary.FullPath,
                "-c",
                "user.name=Other Person",
                "-c",
                "user.email=other@example.invalid",
                "commit",
                "-m",
                "feat: 别人的独占提交");

            // mine 提交：我的独占提交；twin 指向同一个提交（权威 `exclusiveNodes` 对"同一提交的两个分支头"
            // 各自都算独占：该提交没有其它**子提交**分担，见 `GraphUtil.kt:142-158` 的
            // `upNodes.all { result.contains(it) }` 条件）。
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", defaultBranch);
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-c", "mine");
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "mine.txt", "mine\n", "feat: 我的独占提交");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "branch", "twin", "mine");

            GitMyBranchesResult result = await new GitReferenceService(runtime).ReadMyBranchesAsync(repository);

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.AreEqual("augit-tests@example.invalid", result.Author);
            Assert.Contains("mine", result.Mine);
            Assert.Contains("twin", result.Mine);
            Assert.DoesNotContain("their", result.Mine);
            // 默认分支的 tip 是别人分支的祖先 ⇒ 它没有任何"只从自己可达"的提交 ⇒ 不算我的分支。
            Assert.DoesNotContain(defaultBranch, result.Mine);
        }
    }

    [TestMethod]
    public async Task 没有配置Git用户时我的分支给出可读原因()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "base.txt", "base\n", "test: base");
            // 置成空串而不是 --unset：`git config --get` 会继续读到全局配置，空串才能确定性地表达"没有配置"。
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "user.email", "");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "user.name", "");

            GitMyBranchesResult result = await new GitReferenceService(runtime).ReadMyBranchesAsync(repository);

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.ErrorMessage, "Git 用户");
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
