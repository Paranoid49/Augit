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
