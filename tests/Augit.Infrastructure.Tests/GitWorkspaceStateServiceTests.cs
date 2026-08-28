using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Interop;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitWorkspaceStateServiceTests
{
    [TestMethod]
    public async Task Stash包含未跟踪文件并支持查看应用和弹出()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "tracked.txt", "base\n", "test: base");
            await File.WriteAllTextAsync(temporary.GetPath("tracked.txt"), "changed\n");
            await File.WriteAllTextAsync(temporary.GetPath("new.txt"), "new\n");
            GitWorkspaceStateService service = CreateService(runtime, out _);

            GitActionResult stashed = await service.StashAsync(repository, "阶段三测试", includeUntracked: true);
            GitStashListResult list = await service.ReadStashesAsync(repository);
            GitStashContentResult content = await service.ReadStashContentAsync(repository, "stash@{0}");
            GitActionResult applied = await service.UnstashAsync(repository, "stash@{0}", keepStash: true);
            GitStashListResult kept = await service.ReadStashesAsync(repository);
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "reset", "--hard", "HEAD");
            File.Delete(temporary.GetPath("new.txt"));
            GitActionResult popped = await service.UnstashAsync(repository, "stash@{0}", keepStash: false);
            GitStashListResult empty = await service.ReadStashesAsync(repository);

            Assert.IsTrue(stashed.IsSuccess, stashed.ErrorMessage);
            Assert.IsEmpty(stashed.ActualStatus!.Files);
            Assert.HasCount(1, list.Stashes!);
            Assert.Contains("阶段三测试", list.Stashes![0].Subject, StringComparison.Ordinal);
            Assert.IsTrue(content.IsSuccess, content.ErrorMessage);
            Assert.Contains("tracked.txt", content.Document!.UnifiedPatch!, StringComparison.Ordinal);
            Assert.IsTrue(applied.IsSuccess, applied.ErrorMessage);
            Assert.HasCount(2, applied.ActualStatus!.Files);
            Assert.HasCount(1, kept.Stashes!);
            Assert.IsTrue(popped.IsSuccess, popped.ErrorMessage);
            Assert.IsEmpty(empty.Stashes!);
        }
    }

    [TestMethod]
    public async Task Reset三种模式保留或丢弃内容的边界正确()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "value.txt", "one\n", "test: one");
            string first = (await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "rev-parse", "HEAD"))
                .StandardOutput.Trim();
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "value.txt", "two\n", "test: two");
            string second = (await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "rev-parse", "HEAD"))
                .StandardOutput.Trim();
            GitWorkspaceStateService service = CreateService(runtime, out _);

            GitActionResult soft = await service.ResetAsync(repository, first, GitResetMode.Soft);
            Assert.IsTrue(soft.IsSuccess, soft.ErrorMessage);
            Assert.IsTrue(soft.ActualStatus!.Files.Single().HasStagedChanges);
            await service.ResetAsync(repository, second, GitResetMode.Hard);

            GitActionResult mixed = await service.ResetAsync(repository, first, GitResetMode.Mixed);
            Assert.IsTrue(mixed.IsSuccess, mixed.ErrorMessage);
            Assert.IsFalse(mixed.ActualStatus!.Files.Single().HasStagedChanges);
            Assert.IsTrue(mixed.ActualStatus.Files.Single().HasWorkingTreeChanges);
            await service.ResetAsync(repository, second, GitResetMode.Hard);

            GitActionResult hard = await service.ResetAsync(repository, first, GitResetMode.Hard);
            Assert.IsTrue(hard.IsSuccess, hard.ErrorMessage);
            Assert.IsEmpty(hard.ActualStatus!.Files);
            Assert.AreEqual("one\n", (await File.ReadAllTextAsync(temporary.GetPath("value.txt")))
                .Replace("\r\n", "\n", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task Rollback恢复修改重命名并把新增和未跟踪文件交给回收站入口()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "tracked.txt", "base\n", "test: base");
            GitWorkspaceStateService service = CreateService(runtime, out List<string> recycled);
            GitStatusService statusService = new(runtime);

            await File.WriteAllTextAsync(temporary.GetPath("tracked.txt"), "changed\n");
            GitChangedFile modified = (await statusService.ReadAsync(repository)).Snapshot!.Files.Single();
            GitActionResult modifiedRollback = await service.RollbackAsync(repository, modified);

            await File.WriteAllTextAsync(temporary.GetPath("new.txt"), "new\n");
            GitChangedFile untracked = (await statusService.ReadAsync(repository)).Snapshot!.Files.Single();
            GitActionResult untrackedRollback = await service.RollbackAsync(repository, untracked);

            await File.WriteAllTextAsync(temporary.GetPath("added.txt"), "added\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "added.txt");
            GitChangedFile added = (await statusService.ReadAsync(repository)).Snapshot!.Files.Single();
            GitActionResult addedRollback = await service.RollbackAsync(repository, added);

            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "mv", "tracked.txt", "renamed.txt");
            GitChangedFile renamed = (await statusService.ReadAsync(repository)).Snapshot!.Files.Single();
            GitActionResult renamedRollback = await service.RollbackAsync(repository, renamed);

            Assert.IsTrue(modifiedRollback.IsSuccess, modifiedRollback.ErrorMessage);
            Assert.AreEqual("base\n", (await File.ReadAllTextAsync(temporary.GetPath("tracked.txt")))
                .Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.IsTrue(untrackedRollback.IsSuccess, untrackedRollback.ErrorMessage);
            Assert.IsTrue(addedRollback.IsSuccess, addedRollback.ErrorMessage);
            Assert.IsTrue(renamedRollback.IsSuccess, renamedRollback.ErrorMessage);
            Assert.IsTrue(File.Exists(temporary.GetPath("tracked.txt")));
            Assert.IsFalse(File.Exists(temporary.GetPath("renamed.txt")));
            Assert.IsFalse(File.Exists(temporary.GetPath("new.txt")));
            Assert.IsFalse(File.Exists(temporary.GetPath("added.txt")));
            Assert.HasCount(3, recycled);
            Assert.IsEmpty((await statusService.ReadAsync(repository)).Snapshot!.Files);
        }
    }

    [TestMethod]
    public async Task Stash内容超限时不保留正文且非法引用被拒绝()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "large.txt", "base\n", "test: base");
            await File.WriteAllTextAsync(
                temporary.GetPath("large.txt"),
                string.Concat(Enumerable.Repeat("超限内容", 500)));
            GitWorkspaceStateService service = new(
                runtime,
                new GitCommandRunner(TimeSpan.FromSeconds(30), 8 * 1024 * 1024),
                new GitCommandRunner(TimeSpan.FromSeconds(30), 128),
                new GitStatusService(runtime),
                _ => new RecycleBinResult(true, null));

            GitActionResult stashed = await service.StashAsync(repository, null, includeUntracked: false);
            GitStashContentResult content = await service.ReadStashContentAsync(repository, "stash@{0}");
            GitStashContentResult invalidRead = await service.ReadStashContentAsync(repository, "stash@{x}");
            GitActionResult invalidApply = await service.UnstashAsync(repository, "--index", keepStash: true);

            Assert.IsTrue(stashed.IsSuccess, stashed.ErrorMessage);
            Assert.IsTrue(content.IsSuccess, content.ErrorMessage);
            Assert.AreEqual(GitDiffContentStatus.OutputTooLarge, content.Document!.Status);
            Assert.IsNull(content.Document.UnifiedPatch);
            Assert.IsNotNull(content.Document.CopyableCommand);
            Assert.IsFalse(invalidRead.IsSuccess);
            Assert.AreEqual(GitOperationFailureKind.InvalidRequest, invalidRead.FailureKind);
            Assert.IsFalse(invalidApply.IsSuccess);
            Assert.AreEqual(GitOperationFailureKind.InvalidRequest, invalidApply.FailureKind);
        }
    }

    [TestMethod]
    public async Task Reset取消后返回仓库实际状态并结束过滤器子进程()
    {
        (TemporaryDirectory temporary, GitRuntimeInfo runtime, GitRepositorySnapshot repository) = await CreateRepositoryAsync();
        using (temporary)
        {
            string filterPath = temporary.GetPath("slow-filter.cmd");
            await File.WriteAllTextAsync(filterPath, "@echo off\r\nping 127.0.0.1 -n 6 >nul\r\nmore\r\n");
            await GitTestEnvironment.RunAsync(
                runtime,
                temporary.FullPath,
                "config",
                "filter.slow.smudge",
                $"\"{filterPath}\"");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "filter.slow.clean", "cat");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "filter.slow.required", "true");
            await File.WriteAllTextAsync(temporary.GetPath(".gitattributes"), "slow.txt filter=slow\n");
            await File.WriteAllTextAsync(temporary.GetPath("slow.txt"), "one\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", ".gitattributes", "slow.txt");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "commit", "-m", "test: slow one");
            string first = (await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "rev-parse", "HEAD"))
                .StandardOutput.Trim();
            await File.WriteAllTextAsync(temporary.GetPath("slow.txt"), "two\n");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "slow.txt");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "commit", "-m", "test: slow two");
            GitWorkspaceStateService service = CreateService(runtime, out _);
            using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(200));

            GitActionResult result = await service.ResetAsync(
                repository,
                first,
                GitResetMode.Hard,
                cancellation.Token);
            GitStatusResult actual = await new GitStatusService(runtime).ReadAsync(repository);

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(GitOperationFailureKind.Cancelled, result.FailureKind);
            Assert.IsNotNull(result.ActualStatus);
            Assert.IsTrue(actual.IsSuccess, actual.ErrorMessage);
            CollectionAssert.AreEqual(
                actual.Snapshot!.Files.Select(file => file.RelativePath).Order().ToArray(),
                result.ActualStatus.Files.Select(file => file.RelativePath).Order().ToArray());
        }
    }

    private static GitWorkspaceStateService CreateService(
        GitRuntimeInfo runtime,
        out List<string> recycled)
    {
        recycled = [];
        List<string> captured = recycled;
        return new(
            runtime,
            new GitCommandRunner(TimeSpan.FromSeconds(30), 8 * 1024 * 1024),
            new GitCommandRunner(TimeSpan.FromSeconds(30), 20 * 1024 * 1024),
            new GitStatusService(runtime),
            path =>
            {
                captured.Add(path);
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }

                return new RecycleBinResult(true, null);
            });
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
