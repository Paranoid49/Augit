using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitRemoteServiceTests
{
    [TestMethod]
    public async Task Remote可查看新增修改重命名和删除()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string repositoryPath = temporary.GetPath("repository");
        string firstBare = temporary.GetPath("first.git");
        string secondBare = temporary.GetPath("second.git");
        Directory.CreateDirectory(repositoryPath);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "init", "--bare", firstBare);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "init", "--bare", secondBare);
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime).InitializeAsync(repositoryPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        GitRemoteService service = new(runtime);

        GitRemoteOperationResult added = await service.AddRemoteAsync(
            initialized.Repository!,
            "origin",
            firstBare,
            secondBare);
        GitRemoteOperationResult updated = await service.UpdateRemoteAsync(
            initialized.Repository!,
            "origin",
            "upstream",
            secondBare,
            firstBare);
        GitRemoteListResult listed = await service.ReadRemotesAsync(initialized.Repository!);
        GitRemoteOperationResult deleted = await service.DeleteRemoteAsync(initialized.Repository!, "upstream");

        Assert.IsTrue(added.IsSuccess, added.ErrorMessage);
        Assert.IsTrue(updated.IsSuccess, updated.ErrorMessage);
        Assert.IsTrue(listed.IsSuccess, listed.ErrorMessage);
        GitRemoteInfo remote = listed.Remotes!.Single();
        Assert.AreEqual("upstream", remote.Name);
        Assert.AreEqual(secondBare, remote.FetchUrl);
        Assert.AreEqual(firstBare, remote.PushUrl);
        Assert.IsTrue(deleted.IsSuccess, deleted.ErrorMessage);
        Assert.IsEmpty(deleted.ActualRemotes!);
    }

    [TestMethod]
    public async Task Fetch快进Pull普通Push和跟踪关系使用本机Git配置()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string remotePath = temporary.GetPath("remote.git");
        string sourcePath = temporary.GetPath("source");
        string consumerPath = temporary.GetPath("consumer");
        Directory.CreateDirectory(sourcePath);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "init", "--bare", remotePath);
        GitRepositoryService repositoryService = new(runtime);
        GitRepositoryOperationResult sourceInitialized = await repositoryService.InitializeAsync(sourcePath);
        Assert.IsTrue(sourceInitialized.IsSuccess, sourceInitialized.ErrorMessage);
        await GitTestEnvironment.CommitFileAsync(runtime, sourcePath, "shared.txt", "version one\n", "test: initial");
        GitCommandResult branchResult = await GitTestEnvironment.RunAsync(
            runtime,
            sourcePath,
            "branch",
            "--show-current");
        string branch = branchResult.StandardOutput.Trim();
        GitRemoteService sourceRemoteService = new(runtime);
        GitRemoteOperationResult added = await sourceRemoteService.AddRemoteAsync(
            sourceInitialized.Repository!,
            "origin",
            remotePath);
        Assert.IsTrue(added.IsSuccess, added.ErrorMessage);
        GitRemoteOperationResult firstPush = await sourceRemoteService.PushAsync(
            sourceInitialized.Repository!,
            "origin",
            branch);
        Assert.IsTrue(firstPush.IsSuccess, firstPush.ErrorMessage);
        GitRemoteOperationResult tracking = await sourceRemoteService.SetTrackingAsync(
            sourceInitialized.Repository!,
            branch,
            "origin",
            branch);
        Assert.IsTrue(tracking.IsSuccess, tracking.ErrorMessage);

        GitRepositoryOperationResult cloned = await repositoryService.CloneAsync(remotePath, consumerPath);
        Assert.IsTrue(cloned.IsSuccess, cloned.ErrorMessage);
        await GitTestEnvironment.CommitFileAsync(runtime, sourcePath, "shared.txt", "version two\n", "test: update");
        GitRemoteOperationResult sourcePush = await sourceRemoteService.PushAsync(sourceInitialized.Repository!);
        Assert.IsTrue(sourcePush.IsSuccess, sourcePush.ErrorMessage);
        GitRemoteService consumerRemoteService = new(runtime);

        GitRemoteOperationResult fetched = await consumerRemoteService.FetchAsync(cloned.Repository!, "origin");
        GitCommandResult remoteHead = await GitTestEnvironment.RunAsync(
            runtime,
            consumerPath,
            "rev-parse",
            $"origin/{branch}");
        GitCommandResult sourceHead = await GitTestEnvironment.RunAsync(runtime, sourcePath, "rev-parse", "HEAD");
        GitRemoteOperationResult pulled = await consumerRemoteService.PullAsync(
            cloned.Repository!,
            GitPullMode.FastForwardOnly);

        Assert.IsTrue(fetched.IsSuccess, fetched.ErrorMessage);
        Assert.AreEqual(sourceHead.StandardOutput.Trim(), remoteHead.StandardOutput.Trim());
        Assert.IsTrue(pulled.IsSuccess, pulled.ErrorMessage);
        string pulledContent = await File.ReadAllTextAsync(Path.Combine(consumerPath, "shared.txt"));
        Assert.AreEqual("version two\n", pulledContent.Replace("\r\n", "\n", StringComparison.Ordinal));

        await GitTestEnvironment.RunAsync(runtime, consumerPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(
            runtime,
            consumerPath,
            "config",
            "user.email",
            "augit-tests@example.invalid");
        await File.WriteAllTextAsync(Path.Combine(consumerPath, "consumer.txt"), "consumer\n");
        GitCommitResult commit = await new GitCommitService(runtime).CommitAsync(
            cloned.Repository!,
            new(["consumer.txt"], "test: consumer update"));
        Assert.IsTrue(commit.IsSuccess, commit.ErrorMessage);
        GitRemoteOperationResult pushed = await consumerRemoteService.PushAsync(cloned.Repository!);
        GitCommandResult bareHead = await GitTestEnvironment.RunAsync(runtime, remotePath, "rev-parse", "HEAD");
        GitCommandResult consumerHead = await GitTestEnvironment.RunAsync(runtime, consumerPath, "rev-parse", "HEAD");

        Assert.IsTrue(pushed.IsSuccess, pushed.ErrorMessage);
        Assert.AreEqual(consumerHead.StandardOutput.Trim(), bareHead.StandardOutput.Trim());
    }

    [TestMethod]
    public async Task Push预览按上游只列出尚未推送的提交()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string remotePath = temporary.GetPath("remote.git");
        string repositoryPath = temporary.GetPath("repository");
        Directory.CreateDirectory(repositoryPath);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "init", "--bare", remotePath);
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime).InitializeAsync(repositoryPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.CommitFileAsync(runtime, repositoryPath, "first.txt", "first\n", "test: first");
        string branch = (await GitTestEnvironment.RunAsync(runtime, repositoryPath, "branch", "--show-current"))
            .StandardOutput
            .Trim();
        GitRemoteService service = new(runtime);
        GitRemoteOperationResult added = await service.AddRemoteAsync(initialized.Repository!, "origin", remotePath);
        Assert.IsTrue(added.IsSuccess, added.ErrorMessage);
        GitRemoteOperationResult firstPush = await service.PushRefAsync(
            initialized.Repository!,
            "origin",
            $"refs/heads/{branch}",
            $"refs/heads/{branch}");
        Assert.IsTrue(firstPush.IsSuccess, firstPush.ErrorMessage);
        GitRemoteOperationResult tracking = await service.SetTrackingAsync(
            initialized.Repository!,
            branch,
            "origin",
            branch);
        Assert.IsTrue(tracking.IsSuccess, tracking.ErrorMessage);
        await GitTestEnvironment.CommitFileAsync(runtime, repositoryPath, "second.txt", "second\n", "test: second");

        GitPushPreviewResult preview = await service.ReadPushPreviewAsync(initialized.Repository!);

        Assert.IsTrue(preview.IsSuccess, preview.ErrorMessage);
        Assert.IsNotNull(preview.Preview);
        Assert.AreEqual($"refs/heads/{branch}", preview.Preview.LocalReference);
        Assert.AreEqual("origin", preview.Preview.RemoteName);
        Assert.AreEqual($"refs/heads/{branch}", preview.Preview.RemoteReference);
        Assert.HasCount(1, preview.Preview.Commits);
        Assert.AreEqual("test: second", preview.Preview.Commits[0].Subject);

        GitRemoteOperationResult pushed = await service.PushRefAsync(
            initialized.Repository!,
            preview.Preview.RemoteName,
            preview.Preview.LocalReference,
            preview.Preview.RemoteReference);
        Assert.IsTrue(pushed.IsSuccess, pushed.ErrorMessage);
        GitCommandResult localHead = await GitTestEnvironment.RunAsync(runtime, repositoryPath, "rev-parse", "HEAD");
        GitCommandResult remoteHead = await GitTestEnvironment.RunAsync(runtime, remotePath, "rev-parse", branch);
        Assert.AreEqual(localHead.StandardOutput.Trim(), remoteHead.StandardOutput.Trim());
    }

    [TestMethod]
    public async Task Push预览支持无上游分支和明确标签引用()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string remotePath = temporary.GetPath("remote.git");
        string repositoryPath = temporary.GetPath("repository");
        Directory.CreateDirectory(repositoryPath);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "init", "--bare", remotePath);
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime).InitializeAsync(repositoryPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.CommitFileAsync(runtime, repositoryPath, "file.txt", "content\n", "test: initial");
        string branch = (await GitTestEnvironment.RunAsync(runtime, repositoryPath, "branch", "--show-current"))
            .StandardOutput
            .Trim();
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "tag", "v1.0.0");
        GitRemoteService service = new(runtime);
        GitRemoteOperationResult added = await service.AddRemoteAsync(initialized.Repository!, "origin", remotePath);
        Assert.IsTrue(added.IsSuccess, added.ErrorMessage);

        GitPushPreviewResult branchPreview = await service.ReadPushPreviewAsync(initialized.Repository!);
        GitPushPreviewResult tagPreview = await service.ReadPushPreviewAsync(
            initialized.Repository!,
            "refs/tags/v1.0.0");

        Assert.IsTrue(branchPreview.IsSuccess, branchPreview.ErrorMessage);
        Assert.AreEqual($"refs/heads/{branch}", branchPreview.Preview!.LocalReference);
        Assert.AreEqual($"refs/heads/{branch}", branchPreview.Preview.RemoteReference);
        Assert.HasCount(1, branchPreview.Preview.Commits);
        Assert.IsTrue(tagPreview.IsSuccess, tagPreview.ErrorMessage);
        Assert.AreEqual("refs/tags/v1.0.0", tagPreview.Preview!.LocalReference);
        Assert.AreEqual("refs/tags/v1.0.0", tagPreview.Preview.RemoteReference);
        Assert.HasCount(1, tagPreview.Preview.Commits);
    }

    [TestMethod]
    public async Task Push预览明确区分无远端与DetachedHead()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string repositoryPath = temporary.GetPath("repository");
        Directory.CreateDirectory(repositoryPath);
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime).InitializeAsync(repositoryPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.CommitFileAsync(runtime, repositoryPath, "file.txt", "content\n", "test: initial");
        string branch = (await GitTestEnvironment.RunAsync(runtime, repositoryPath, "branch", "--show-current"))
            .StandardOutput
            .Trim();
        GitRemoteService service = new(runtime);

        GitPushPreviewResult noRemote = await service.ReadPushPreviewAsync(initialized.Repository!);
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "checkout", "--detach");
        GitPushPreviewResult detached = await service.ReadPushPreviewAsync(initialized.Repository!);

        Assert.IsFalse(noRemote.IsSuccess);
        StringAssert.Contains(noRemote.ErrorMessage, "未配置远端");
        Assert.IsTrue(noRemote.CanDefineRemote);
        Assert.AreEqual($"refs/heads/{branch}", noRemote.LocalReference);
        Assert.IsFalse(detached.IsSuccess);
        StringAssert.Contains(detached.ErrorMessage, "detached HEAD");
        Assert.IsFalse(detached.CanDefineRemote);
    }

    [TestMethod]
    public async Task 需要交互输入时返回稳定原因且不暴露原命令()
    {
        using TemporaryDirectory temporary = new();

        GitCommandResult result = await new GitCommandRunner().RunAsync(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            temporary.FullPath,
            ["/d", "/c", "echo fatal: could not read Username because terminal prompts disabled 1>&2 & exit /b 1"],
            GitCommandMode.Network);

        Assert.AreEqual(GitOperationFailureKind.InteractiveInputRequired, result.FailureKind);
        Assert.AreEqual("Git 需要交互输入，图形操作已停止。", result.ErrorMessage);
        Assert.DoesNotContain("cmd.exe", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Remote输出解析保留不同Fetch和Push地址()
    {
        const string Output = "origin\thttps://example.invalid/fetch.git (fetch)\norigin\tssh://example.invalid/push.git (push)\n";

        bool parsed = GitRemoteService.TryParseRemotes(Output, out IReadOnlyList<GitRemoteInfo>? remotes);

        Assert.IsTrue(parsed);
        GitRemoteInfo remote = remotes!.Single();
        Assert.AreEqual("https://example.invalid/fetch.git", remote.FetchUrl);
        Assert.AreEqual("ssh://example.invalid/push.git", remote.PushUrl);
    }
}
