using System.Text;
using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitConflictServiceTests
{
    [TestMethod]
    public async Task 文本冲突加载三侧内容并可保存人工组合结果()
    {
        ConflictRepository setup = await CreateMergeConflictAsync(
            "base\n"u8.ToArray(),
            "当前内容\n"u8.ToArray(),
            "合入内容\n"u8.ToArray());
        using (setup.Temporary)
        {
            GitOperationService operationService = new(setup.Runtime);
            GitConflictService service = new(setup.Runtime, operationService);

            GitConflictLoadResult loaded = await service.LoadAsync(setup.Repository, "conflict.txt");

            Assert.IsTrue(loaded.IsSuccess, loaded.ErrorMessage);
            Assert.AreEqual(GitConflictContentKind.Text, loaded.Document!.ContentKind);
            Assert.Contains("当前内容", loaded.Document.YoursText!);
            Assert.Contains("合入内容", loaded.Document.TheirsText!);
            Assert.HasCount(1, loaded.Document.Blocks);
            Assert.IsTrue(GitConflictText.TryResolveBlock(
                loaded.Document.ResultText!,
                0,
                GitConflictBlockChoice.Both,
                out string? combined));

            GitConflictMutationResult saved = await service.SaveResolvedAsync(
                setup.Repository,
                new(
                    "conflict.txt",
                    combined!,
                    loaded.Document.FileVersion!,
                    loaded.Document.Operation));

            Assert.IsTrue(saved.IsSuccess, saved.ErrorMessage);
            Assert.IsFalse(saved.Session!.HasConflicts);
            string actual = (await File.ReadAllTextAsync(setup.Temporary.GetPath("conflict.txt")))
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.AreEqual("当前内容\n合入内容\n", actual);
        }
    }

    [TestMethod]
    public async Task 已取消的保存不继续解析冲突或改写工作区()
    {
        ConflictRepository setup = await CreateMergeConflictAsync(
            "base\n"u8.ToArray(), "current\n"u8.ToArray(), "incoming\n"u8.ToArray());
        using (setup.Temporary)
        {
            GitConflictService service = new(setup.Runtime);
            GitConflictLoadResult loaded = await service.LoadAsync(setup.Repository, "conflict.txt");
            Assert.IsTrue(loaded.IsSuccess, loaded.ErrorMessage);
            GitConflictDocument document = loaded.Document!;
            byte[] before = await File.ReadAllBytesAsync(setup.Temporary.GetPath("conflict.txt"));
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.SaveResolvedAsync(
                setup.Repository,
                new("conflict.txt", document.ResultText!, document.FileVersion!, document.Operation),
                cancellation.Token));

            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(setup.Temporary.GetPath("conflict.txt")));
            GitAdvancedOperationResult actual = await new GitOperationService(setup.Runtime).InspectAsync(setup.Repository);
            Assert.IsTrue(actual.IsSuccess, actual.ErrorMessage);
            Assert.IsTrue(actual.Session!.HasConflicts);
            Assert.IsEmpty(Directory.GetFiles(setup.Temporary.FullPath, ".augit-conflict-*.tmp"));
        }
    }

    [TestMethod]
    public async Task 外部结果重载重新解析完整块并忽略未闭合块()
    {
        ConflictRepository setup = await CreateMergeConflictAsync(
            "base\n"u8.ToArray(), "current\n"u8.ToArray(), "incoming\n"u8.ToArray());
        using (setup.Temporary)
        {
            GitConflictService service = new(setup.Runtime);
            GitConflictLoadResult loaded = await service.LoadAsync(setup.Repository, "conflict.txt");
            Assert.IsTrue(loaded.IsSuccess, loaded.ErrorMessage);
            const string prefix = "已编辑😀\r\n<<<<<<< unfinished\r\n未完成\r\n";
            const string complete = "<<<<<<< HEAD\r\n左\r\n||||||| base\r\n祖先\r\n=======\r\n右\r\n>>>>>>> incoming";
            await File.WriteAllTextAsync(setup.Temporary.GetPath("conflict.txt"), prefix + complete);

            GitConflictLoadResult reloaded = await service.ReloadWorkingFileAsync(setup.Repository, loaded.Document!);

            Assert.IsTrue(reloaded.IsSuccess, reloaded.ErrorMessage);
            Assert.AreEqual(prefix + complete, reloaded.Document!.ResultText);
            Assert.AreEqual(loaded.Document!.YoursText, reloaded.Document.YoursText);
            Assert.AreEqual(loaded.Document.TheirsText, reloaded.Document.TheirsText);
            Assert.AreNotEqual(loaded.Document.FileVersion, reloaded.Document.FileVersion);
            GitConflictBlock block = reloaded.Document.Blocks.Single();
            Assert.AreEqual(prefix.Length, block.Start);
            Assert.AreEqual(complete.Length, block.Length);
            Assert.AreEqual("左\r\n", block.YoursText);
            Assert.AreEqual("祖先\r\n", block.AncestorText);
            Assert.AreEqual("右\r\n", block.TheirsText);
        }
    }

    [TestMethod]
    public async Task 保存前外部文件变化会被拒绝且不会覆盖外部内容()
    {
        ConflictRepository setup = await CreateMergeConflictAsync(
            "base\n"u8.ToArray(),
            "current\n"u8.ToArray(),
            "incoming\n"u8.ToArray());
        using (setup.Temporary)
        {
            GitOperationService operationService = new(setup.Runtime);
            GitConflictService service = new(setup.Runtime, operationService);
            GitConflictLoadResult loaded = await service.LoadAsync(setup.Repository, "conflict.txt");
            await File.WriteAllTextAsync(setup.Temporary.GetPath("conflict.txt"), "外部工具结果\n");

            GitConflictMutationResult saved = await service.SaveResolvedAsync(
                setup.Repository,
                new(
                    "conflict.txt",
                    "编辑器结果\n",
                    loaded.Document!.FileVersion!,
                    loaded.Document.Operation));

            Assert.IsFalse(saved.IsSuccess);
            Assert.AreEqual(GitOperationFailureKind.InvalidRequest, saved.FailureKind);
            Assert.Contains("外部工具结果", await File.ReadAllTextAsync(setup.Temporary.GetPath("conflict.txt")));
        }
    }

    [TestMethod]
    public async Task 外部工具标记已解决后操作会话立即读取最新状态()
    {
        ConflictRepository setup = await CreateMergeConflictAsync(
            "base\n"u8.ToArray(),
            "current\n"u8.ToArray(),
            "incoming\n"u8.ToArray());
        using (setup.Temporary)
        {
            GitOperationService service = new(setup.Runtime);
            await File.WriteAllTextAsync(setup.Temporary.GetPath("conflict.txt"), "外部解决\n");
            await GitTestEnvironment.RunAsync(
                setup.Runtime,
                setup.Temporary.FullPath,
                "add",
                "--",
                "conflict.txt");

            GitAdvancedOperationResult state = await service.InspectAsync(setup.Repository);

            Assert.IsTrue(state.IsSuccess, state.ErrorMessage);
            Assert.IsFalse(state.Session!.HasConflicts);
            Assert.IsTrue(state.Session.CanContinue);
        }
    }

    [TestMethod]
    public async Task 二进制冲突不进入文本编辑且可接受合入侧()
    {
        ConflictRepository setup = await CreateMergeConflictAsync(
            [0, 1, 2],
            [0, 3, 4],
            [0, 5, 6]);
        using (setup.Temporary)
        {
            GitOperationService operationService = new(setup.Runtime);
            GitConflictService service = new(setup.Runtime, operationService);

            GitConflictLoadResult loaded = await service.LoadAsync(setup.Repository, "conflict.txt");
            GitConflictMutationResult accepted = await service.AcceptSideAsync(
                setup.Repository,
                "conflict.txt",
                GitConflictSide.Theirs);

            Assert.IsTrue(loaded.IsSuccess, loaded.ErrorMessage);
            Assert.AreEqual(GitConflictContentKind.Binary, loaded.Document!.ContentKind);
            Assert.IsNull(loaded.Document.ResultText);
            Assert.IsTrue(accepted.IsSuccess, accepted.ErrorMessage);
            CollectionAssert.AreEqual(
                new byte[] { 0, 5, 6 },
                await File.ReadAllBytesAsync(setup.Temporary.GetPath("conflict.txt")));
        }
    }

    [TestMethod]
    public async Task 非法UTF8冲突不进入文本编辑()
    {
        ConflictRepository setup = await CreateMergeConflictAsync(
            [0xFF, 0x01],
            [0xFF, 0x02],
            [0xFF, 0x03]);
        using (setup.Temporary)
        {
            GitOperationService operationService = new(setup.Runtime);
            GitConflictService service = new(setup.Runtime, operationService);

            GitConflictLoadResult loaded = await service.LoadAsync(setup.Repository, "conflict.txt");

            Assert.IsTrue(loaded.IsSuccess, loaded.ErrorMessage);
            Assert.AreEqual(GitConflictContentKind.InvalidUtf8, loaded.Document!.ContentKind);
            Assert.IsNull(loaded.Document.ResultText);
        }
    }

    [TestMethod]
    public async Task 超过十MB的冲突不读取正文()
    {
        byte[] baseContent = CreateLargeContent((byte)'0');
        byte[] yours = CreateLargeContent((byte)'1');
        byte[] theirs = CreateLargeContent((byte)'2');
        ConflictRepository setup = await CreateMergeConflictAsync(baseContent, yours, theirs);
        using (setup.Temporary)
        {
            GitOperationService operationService = new(setup.Runtime);
            GitConflictService service = new(setup.Runtime, operationService);

            GitConflictLoadResult loaded = await service.LoadAsync(setup.Repository, "conflict.txt");

            Assert.IsTrue(loaded.IsSuccess, loaded.ErrorMessage);
            Assert.AreEqual(GitConflictContentKind.TooLarge, loaded.Document!.ContentKind);
            Assert.IsNull(loaded.Document.YoursText);
            Assert.IsNull(loaded.Document.TheirsText);
            Assert.IsNull(loaded.Document.ResultText);
        }
    }

    [TestMethod]
    public async Task 接受不存在的一侧会使用Git删除冲突文件()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        TemporaryDirectory temporary = new();
        using (temporary)
        {
            GitRepositoryOperationResult initialized = await InitializeAsync(runtime, temporary);
            await CommitBytesAsync(runtime, temporary.FullPath, "conflict.txt", "base\n"u8.ToArray(), "test: base");
            string main = await CurrentBranchAsync(runtime, temporary.FullPath);
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "checkout", "-b", "delete-file");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "rm", "--", "conflict.txt");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "commit", "-m", "test: delete");
            await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "checkout", main);
            await CommitBytesAsync(runtime, temporary.FullPath, "conflict.txt", "current\n"u8.ToArray(), "test: current");
            GitCommandResult merge = await new GitCommandRunner().RunAsync(
                runtime.ExecutablePath!,
                temporary.FullPath,
                ["merge", "delete-file"],
                GitCommandMode.LocalWrite);
            Assert.IsFalse(merge.IsSuccess);
            GitOperationService operationService = new(runtime);
            GitConflictService service = new(runtime, operationService);

            GitConflictMutationResult accepted = await service.AcceptSideAsync(
                initialized.Repository!,
                "conflict.txt",
                GitConflictSide.Theirs);

            Assert.IsTrue(accepted.IsSuccess, accepted.ErrorMessage);
            Assert.IsFalse(File.Exists(temporary.GetPath("conflict.txt")));
            Assert.IsFalse(accepted.Session!.HasConflicts);
        }
    }

    private static async Task<ConflictRepository> CreateMergeConflictAsync(
        byte[] baseContent,
        byte[] yours,
        byte[] theirs)
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        TemporaryDirectory temporary = new();
        GitRepositoryOperationResult initialized = await InitializeAsync(runtime, temporary);
        await CommitBytesAsync(runtime, temporary.FullPath, "conflict.txt", baseContent, "test: base");
        string main = await CurrentBranchAsync(runtime, temporary.FullPath);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "checkout", "-b", "incoming");
        await CommitBytesAsync(runtime, temporary.FullPath, "conflict.txt", theirs, "test: incoming");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "checkout", main);
        await CommitBytesAsync(runtime, temporary.FullPath, "conflict.txt", yours, "test: current");
        GitCommandResult merge = await new GitCommandRunner().RunAsync(
            runtime.ExecutablePath!,
            temporary.FullPath,
            ["merge", "incoming"],
            GitCommandMode.LocalWrite);
        Assert.IsFalse(merge.IsSuccess, "测试仓库必须形成冲突。", merge.ErrorMessage);
        return new(temporary, runtime, initialized.Repository!);
    }

    private static async Task<GitRepositoryOperationResult> InitializeAsync(
        GitRuntimeInfo runtime,
        TemporaryDirectory temporary)
    {
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
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "core.autocrlf", "false");
        return initialized;
    }

    private static async Task CommitBytesAsync(
        GitRuntimeInfo runtime,
        string repositoryPath,
        string relativePath,
        byte[] content,
        string message)
    {
        await File.WriteAllBytesAsync(Path.Combine(repositoryPath, relativePath), content);
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "add", "--", relativePath);
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "commit", "-m", message);
    }

    private static async Task<string> CurrentBranchAsync(GitRuntimeInfo runtime, string repositoryPath)
    {
        return (await GitTestEnvironment.RunAsync(runtime, repositoryPath, "branch", "--show-current"))
            .StandardOutput.Trim();
    }

    private static byte[] CreateLargeContent(byte value)
    {
        byte[] content = new byte[(10 * 1024 * 1024) + 1];
        content.AsSpan().Fill((byte)'a');
        content[0] = value;
        content[1] = (byte)'\n';
        return content;
    }

    private sealed record ConflictRepository(
        TemporaryDirectory Temporary,
        GitRuntimeInfo Runtime,
        GitRepositorySnapshot Repository);
}
