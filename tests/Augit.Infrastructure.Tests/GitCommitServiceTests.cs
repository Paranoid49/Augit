using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitCommitServiceTests
{
    [TestMethod]
    public async Task 只提交勾选文件且不带入外部暂存文件()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await File.WriteAllTextAsync(temporary.GetPath("selected.txt"), "old selected\n");
            await File.WriteAllTextAsync(temporary.GetPath("unselected.txt"), "old unselected\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "selected.txt", "unselected.txt");
            await CommitDirectlyAsync(runtime, temporary.FullPath, "test: base");
            await File.WriteAllTextAsync(temporary.GetPath("selected.txt"), "new selected\n");
            await File.WriteAllTextAsync(temporary.GetPath("unselected.txt"), "new unselected\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "unselected.txt");

            GitCommitResult result = await new GitCommitService(runtime).CommitAsync(
                repository,
                new(["selected.txt"], "test: selected only"));

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.AreEqual("new selected", (await ReadHeadFileAsync(runtime, temporary.FullPath, "selected.txt")).Trim());
            Assert.AreEqual("old unselected", (await ReadHeadFileAsync(runtime, temporary.FullPath, "unselected.txt")).Trim());
            GitChangedFile remaining = result.ActualStatus!.Files.Single();
            Assert.AreEqual("unselected.txt", remaining.RelativePath);
            Assert.IsTrue(remaining.HasStagedChanges);
        }
    }

    [TestMethod]
    public async Task 选中的未跟踪文件先Add再Only提交且其他暂存项保留()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await File.WriteAllTextAsync(temporary.GetPath("selected-new.txt"), "selected\n");
            await File.WriteAllTextAsync(temporary.GetPath("staged-new.txt"), "staged\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "staged-new.txt");

            GitCommitResult result = await new GitCommitService(runtime).CommitAsync(
                repository,
                new(["selected-new.txt"], "test: selected new"));

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.AreEqual("selected", (await ReadHeadFileAsync(runtime, temporary.FullPath, "selected-new.txt")).Trim());
            GitCommandResult missing = await new GitCommandRunner().RunAsync(
                runtime.ExecutablePath!,
                temporary.FullPath,
                ["show", "HEAD:staged-new.txt"],
                GitCommandMode.LocalQuery);
            Assert.IsFalse(missing.IsSuccess);
            GitChangedFile remaining = result.ActualStatus!.Files.Single();
            Assert.AreEqual("staged-new.txt", remaining.RelativePath);
            Assert.IsTrue(remaining.HasStagedChanges);
        }
    }

    [TestMethod]
    public async Task Amend可只修改提交信息且不带入当前暂存内容()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "base.txt", "base\n", "test: base");
            await File.WriteAllTextAsync(temporary.GetPath("staged.txt"), "staged\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "staged.txt");

            GitCommitResult result = await new GitCommitService(runtime).CommitAsync(
                repository,
                new([], "test: amended message", Amend: true));

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            GitCommandResult subject = await GitTestEnvironment.RunAsync(
                runtime,
                temporary.FullPath,
                "log",
                "-1",
                "--format=%s");
            Assert.AreEqual("test: amended message", subject.StandardOutput.Trim());
            Assert.AreEqual("base", (await ReadHeadFileAsync(runtime, temporary.FullPath, "base.txt")).Trim());
            Assert.AreEqual("staged.txt", result.ActualStatus!.Files.Single().RelativePath);
            Assert.IsTrue(result.ActualStatus.Files.Single().HasStagedChanges);
        }
    }

    [TestMethod]
    public async Task 读取上一次提交信息保留标题和正文并去除传输末尾换行()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            const string message = "feat: 提交标题\n\n提交正文第一行\n提交正文第二行";
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "base.txt", "base\n", message);

            GitCommitMessageResult result = await new GitCommitService(runtime)
                .ReadLastCommitMessageAsync(repository);

            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
            Assert.AreEqual(message, result.Message);
        }
    }

    [TestMethod]
    public async Task 无HEAD时读取上一次提交信息返回稳定失败而不伪造空提交()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            GitCommitMessageResult result = await new GitCommitService(runtime)
                .ReadLastCommitMessageAsync(repository);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNull(result.Message);
            Assert.AreNotEqual(GitOperationFailureKind.None, result.FailureKind);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
        }
    }

    [TestMethod]
    public async Task 无仓库规则时在调用Git前执行兜底校验()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await File.WriteAllTextAsync(temporary.GetPath("file.txt"), "content\n");

            GitCommitResult result = await new GitCommitService(runtime).CommitAsync(
                repository,
                new(["file.txt"], "invalid message"));

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(GitOperationFailureKind.InvalidRequest, result.FailureKind);
            Assert.IsFalse(await HasHeadAsync(runtime, temporary.FullPath));
            GitStatusResult status = await new GitStatusService(runtime).ReadAsync(repository);
            Assert.AreEqual(GitChangeGroup.UnversionedFiles, status.Snapshot!.Files.Single().Group);
        }
    }

    [TestMethod]
    public async Task 自定义HooksPath优先于兜底规则且Hook失败输出返回当前界面()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            string hooksDirectory = temporary.GetPath(".custom-hooks");
            Directory.CreateDirectory(hooksDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(hooksDirectory, "commit-msg"),
                "#!/bin/sh\necho hook-blocked >&2\nexit 1\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "core.hooksPath", ".custom-hooks");
            await File.WriteAllTextAsync(temporary.GetPath("file.txt"), "content\n");
            GitCommitService service = new(runtime);

            GitCommitPolicyResult policy = await service.ReadPolicyAsync(repository);
            GitCommitResult result = await service.CommitAsync(
                repository,
                new(["file.txt"], "invalid but repository controlled"));

            Assert.IsTrue(policy.IsSuccess, policy.ErrorMessage);
            Assert.IsTrue(policy.Policy!.HasCommitMessageHook);
            Assert.IsFalse(result.IsSuccess);
            Assert.Contains("hook-blocked", result.ErrorMessage!, StringComparison.Ordinal);
            Assert.IsFalse(await HasHeadAsync(runtime, temporary.FullPath));
        }
    }

    [TestMethod]
    public async Task 可识别Commitlint配置时仓库规则优先()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await File.WriteAllTextAsync(temporary.GetPath("commitlint.config.js"), "module.exports = {};\n");
            await File.WriteAllTextAsync(temporary.GetPath("file.txt"), "content\n");
            GitCommitService service = new(runtime);

            GitCommitPolicyResult policy = await service.ReadPolicyAsync(repository);
            GitCommitResult result = await service.CommitAsync(
                repository,
                new(["file.txt"], "repository decides this message"));

            Assert.IsTrue(policy.Policy!.HasCommitlintConfiguration);
            Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        }
    }

    private static async Task CommitDirectlyAsync(GitRuntimeInfo runtime, string repositoryPath, string message)
    {
        await GitTestEnvironment.RunAsync(
            runtime,
            repositoryPath,
            "-c",
            "user.name=Augit Tests",
            "-c",
            "user.email=augit-tests@example.invalid",
            "commit",
            "-m",
            message);
    }

    private static async Task<string> ReadHeadFileAsync(
        GitRuntimeInfo runtime,
        string repositoryPath,
        string relativePath)
    {
        GitCommandResult result = await GitTestEnvironment.RunAsync(
            runtime,
            repositoryPath,
            "show",
            $"HEAD:{relativePath}");
        return result.StandardOutput;
    }

    private static async Task<bool> HasHeadAsync(GitRuntimeInfo runtime, string repositoryPath)
    {
        GitCommandResult result = await new GitCommandRunner().RunAsync(
            runtime.ExecutablePath!,
            repositoryPath,
            ["rev-parse", "--verify", "HEAD"],
            GitCommandMode.LocalQuery);
        return result.IsSuccess;
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
