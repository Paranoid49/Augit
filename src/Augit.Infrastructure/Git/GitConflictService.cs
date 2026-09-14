using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Augit.Core.Documents;
using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

public sealed class GitConflictService : IGitConflictService
{
    private const long MaximumConflictBytes = DocumentLimits.MaximumTextBytes;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly GitRuntimeInfo _runtime;
    private readonly GitCommandRunner _runner;
    private readonly GitOperationService _operationService;
    private readonly GitStatusService _statusService;

    public GitConflictService(GitRuntimeInfo runtime)
        : this(runtime, new GitOperationService(runtime))
    {
    }

    public GitConflictService(GitRuntimeInfo runtime, GitOperationService operationService)
        : this(
            runtime,
            operationService,
            new GitCommandRunner(TimeSpan.FromSeconds(30), checked((int)MaximumConflictBytes + 1)))
    {
    }

    internal GitConflictService(
        GitRuntimeInfo runtime,
        GitOperationService operationService,
        GitCommandRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(operationService);
        if (!runtime.IsAvailable || string.IsNullOrWhiteSpace(runtime.ExecutablePath))
        {
            throw new ArgumentException("Git 运行环境不可用。", nameof(runtime));
        }

        _runtime = runtime;
        _runner = runner;
        _operationService = operationService;
        _statusService = new(runtime, runner);
    }

    public async Task<GitConflictLoadResult> LoadAsync(
        GitRepositorySnapshot repository,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolvePath(
                repository,
                relativePath,
                out string? root,
                out string? normalizedPath,
                out string? fullPath,
                out string? validationError))
        {
            return GitConflictLoadResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                validationError!);
        }

        GitAdvancedOperationResult operation = await _operationService.InspectAsync(
            repository,
            cancellationToken).ConfigureAwait(false);
        if (!operation.IsSuccess || operation.Session is null)
        {
            return GitConflictLoadResult.Failure(
                operation.FailureKind,
                operation.ErrorMessage ?? "无法读取冲突状态。");
        }

        if (!operation.Session.ConflictFiles.Any(file =>
                file.RelativePath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            return GitConflictLoadResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "所选文件已不再处于冲突状态。");
        }

        GitConflictIndexEntry? entry = await ReadConflictEntryAsync(
            root!,
            normalizedPath!,
            cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return GitConflictLoadResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "无法读取所选文件的 Git 冲突阶段。");
        }

        FileContent resultContent = await ReadWorkingFileAsync(fullPath!, cancellationToken).ConfigureAwait(false);
        FileContent yours = await ReadObjectAsync(root!, normalizedPath!, entry.YoursObject, cancellationToken)
            .ConfigureAwait(false);
        FileContent theirs = await ReadObjectAsync(root!, normalizedPath!, entry.TheirsObject, cancellationToken)
            .ConfigureAwait(false);
        GitConflictContentKind kind = GetOverallKind(entry, resultContent, yours, theirs);
        string? resultText = kind == GitConflictContentKind.Text
            ? Decode(resultContent.Bytes ?? [])
            : null;
        GitConflictFileVersion version = CreateVersion(fullPath!, resultContent.Bytes);
        GitConflictDocument document = new(
            normalizedPath!,
            kind,
            operation.Session.CurrentBranch is null
                ? "当前分支"
                : $"当前分支 · {operation.Session.CurrentBranch}",
            "合入内容",
            kind == GitConflictContentKind.Text && entry.YoursObject is not null
                ? Decode(yours.Bytes ?? [])
                : null,
            kind == GitConflictContentKind.Text && entry.TheirsObject is not null
                ? Decode(theirs.Bytes ?? [])
                : null,
            resultText,
            resultText is null ? [] : GitConflictText.Parse(resultText, cancellationToken),
            version,
            operation.Session.Kind);
        return GitConflictLoadResult.Success(document);
    }

    public async Task<GitConflictMutationResult> SaveResolvedAsync(
        GitRepositorySnapshot repository,
        GitConflictSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryResolvePath(
                repository,
                request.RelativePath,
                out string? root,
                out string? normalizedPath,
                out string? fullPath,
                out string? validationError))
        {
            return GitConflictMutationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                validationError!);
        }

        byte[] content;
        try
        {
            content = StrictUtf8.GetBytes(request.ResultText);
        }
        catch (EncoderFallbackException)
        {
            return GitConflictMutationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "最终结果包含无法编码为 UTF-8 的字符。");
        }

        if (content.LongLength > MaximumConflictBytes || content.Contains((byte)0))
        {
            return GitConflictMutationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "最终结果必须是不超过 10 MB 的有效 UTF-8 文本。");
        }

        if (GitConflictText.Parse(request.ResultText, cancellationToken).Count > 0)
        {
            return GitConflictMutationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "最终结果仍包含未处理的冲突块。");
        }

        GitAdvancedOperationResult before = await _operationService.InspectAsync(
            repository,
            cancellationToken).ConfigureAwait(false);
        if (!before.IsSuccess || before.Session is null)
        {
            return MutationFailure(before);
        }

        if (before.Session.Kind != request.ExpectedOperation
            || !before.Session.ConflictFiles.Any(file =>
                file.RelativePath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            return GitConflictMutationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Git 操作状态或冲突文件已经变化，未保存结果。",
                before.Session,
                before.ActualStatus);
        }

        GitConflictFileVersion actualVersion;
        try
        {
            actualVersion = await ReadVersionAsync(fullPath!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return GitConflictMutationResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "无法确认冲突文件是否被外部修改，未保存结果。",
                before.Session,
                before.ActualStatus);
        }

        if (actualVersion != request.ExpectedVersion)
        {
            return GitConflictMutationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "冲突文件已被外部工具修改，请先选择重新载入或保留当前编辑内容。",
                before.Session,
                before.ActualStatus);
        }

        if (File.Exists(fullPath)
            && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            return GitConflictMutationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "冲突结果不能写入符号链接或其他重解析点。",
                before.Session,
                before.ActualStatus);
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is null)
        {
            return GitConflictMutationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "冲突文件路径无效。",
                before.Session,
                before.ActualStatus);
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".augit-conflict-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, fullPath!, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return GitConflictMutationResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "无法写入冲突最终结果。",
                before.Session,
                before.ActualStatus);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        GitCommandResult add = await RunAsync(
            root!,
            ["add", "--", normalizedPath!],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
        GitAdvancedOperationResult actual = await _operationService.InspectAsync(repository, CancellationToken.None)
            .ConfigureAwait(false);
        return add.IsSuccess
            ? MutationSuccess(actual)
            : GitConflictMutationResult.Failure(
                NormalizeFailure(add),
                add.ErrorMessage,
                actual.Session,
                actual.ActualStatus);
    }

    public async Task<GitConflictLoadResult> ReloadWorkingFileAsync(
        GitRepositorySnapshot repository,
        GitConflictDocument previous,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (!TryResolvePath(
                repository,
                previous.RelativePath,
                out string? root,
                out string? normalizedPath,
                out string? fullPath,
                out string? validationError))
        {
            return GitConflictLoadResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                validationError!);
        }

        GitConflictIndexEntry? entry = await ReadConflictEntryAsync(
            root!,
            normalizedPath!,
            cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return GitConflictLoadResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "所选文件已不再处于冲突状态。");
        }

        FileContent result = await ReadWorkingFileAsync(fullPath!, cancellationToken).ConfigureAwait(false);
        if (result.Kind is not (GitConflictContentKind.Text or GitConflictContentKind.Missing))
        {
            return GitConflictLoadResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "外部工具写入的结果已不再是不超过 10 MB 的有效 UTF-8 文本。");
        }

        string text = result.Kind == GitConflictContentKind.Missing
            ? string.Empty
            : Decode(result.Bytes ?? []);
        return GitConflictLoadResult.Success(previous with
        {
            RelativePath = normalizedPath!,
            ResultText = text,
            Blocks = GitConflictText.Parse(text, cancellationToken),
            FileVersion = CreateVersion(fullPath!, result.Bytes),
        });
    }

    public async Task<GitConflictMutationResult> AcceptSideAsync(
        GitRepositorySnapshot repository,
        string relativePath,
        GitConflictSide side,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolvePath(
                repository,
                relativePath,
                out string? root,
                out string? normalizedPath,
                out _,
                out string? validationError))
        {
            return GitConflictMutationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                validationError!);
        }

        GitAdvancedOperationResult before = await _operationService.InspectAsync(
            repository,
            cancellationToken).ConfigureAwait(false);
        if (!before.IsSuccess || before.Session is null)
        {
            return MutationFailure(before);
        }

        GitConflictFileInfo? file = before.Session.ConflictFiles.FirstOrDefault(conflict =>
            conflict.RelativePath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            return GitConflictMutationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "所选文件已不再处于冲突状态。",
                before.Session,
                before.ActualStatus);
        }

        bool sideExists = side == GitConflictSide.Yours ? file.HasYours : file.HasTheirs;
        GitCommandResult selected = sideExists
            ? await RunAsync(
                root!,
                ["checkout", side == GitConflictSide.Yours ? "--ours" : "--theirs", "--", normalizedPath!],
                GitCommandMode.LocalWrite,
                cancellationToken).ConfigureAwait(false)
            : await RunAsync(
                root!,
                ["rm", "--", normalizedPath!],
                GitCommandMode.LocalWrite,
                cancellationToken).ConfigureAwait(false);
        if (!selected.IsSuccess)
        {
            GitAdvancedOperationResult failedActual = await _operationService.InspectAsync(
                repository,
                CancellationToken.None).ConfigureAwait(false);
            return GitConflictMutationResult.Failure(
                NormalizeFailure(selected),
                selected.ErrorMessage,
                failedActual.Session,
                failedActual.ActualStatus);
        }

        if (sideExists)
        {
            GitCommandResult add = await RunAsync(
                root!,
                ["add", "--", normalizedPath!],
                GitCommandMode.LocalWrite,
                cancellationToken).ConfigureAwait(false);
            if (!add.IsSuccess)
            {
                GitAdvancedOperationResult failedActual = await _operationService.InspectAsync(
                    repository,
                    CancellationToken.None).ConfigureAwait(false);
                return GitConflictMutationResult.Failure(
                    NormalizeFailure(add),
                    add.ErrorMessage,
                    failedActual.Session,
                    failedActual.ActualStatus);
            }
        }

        GitAdvancedOperationResult actual = await _operationService.InspectAsync(repository, CancellationToken.None)
            .ConfigureAwait(false);
        return MutationSuccess(actual);
    }

    private async Task<GitConflictIndexEntry?> ReadConflictEntryAsync(
        string repositoryRoot,
        string relativePath,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await RunAsync(
            repositoryRoot,
            ["ls-files", "--unmerged", "-z", "--", relativePath],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess
            || result.IsOutputTruncated
            || !GitConflictIndex.TryParse(result.StandardOutput, out IReadOnlyList<GitConflictIndexEntry>? entries))
        {
            return null;
        }

        return entries!.SingleOrDefault(entry =>
            entry.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<FileContent> ReadObjectAsync(
        string repositoryRoot,
        string relativePath,
        string? objectId,
        CancellationToken cancellationToken)
    {
        if (objectId is null)
        {
            return FileContent.Missing();
        }

        GitCommandResult size = await RunAsync(
            repositoryRoot,
            ["cat-file", "-s", objectId],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!size.IsSuccess
            || !long.TryParse(size.StandardOutput.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long length))
        {
            return FileContent.Failed();
        }

        if (length > MaximumConflictBytes)
        {
            return FileContent.TooLarge();
        }

        GitCommandResult content = await _runner.RunWithRawOutputAsync(
            _runtime.ExecutablePath!,
            repositoryRoot,
            ["cat-file", "blob", objectId],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        return content.IsSuccess && !content.IsOutputTruncated && content.RawStandardOutput is not null
            ? Classify(relativePath, content.RawStandardOutput)
            : FileContent.Failed();
    }

    private static async Task<FileContent> ReadWorkingFileAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(fullPath))
        {
            return FileContent.Missing();
        }

        FileInfo file = new(fullPath);
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return FileContent.Binary();
        }

        if (file.Length > MaximumConflictBytes)
        {
            return FileContent.TooLarge();
        }

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            return Classify(fullPath, bytes);
        }
        catch (FileNotFoundException)
        {
            return FileContent.Missing();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FileContent.Failed();
        }
    }

    private static FileContent Classify(string path, byte[] bytes)
    {
        DocumentClassification classification = DocumentClassifier.Classify(path, bytes);
        if (classification.IsText)
        {
            return FileContent.Text(bytes);
        }

        return classification.Kind == DocumentKind.InvalidUtf8
            ? FileContent.InvalidUtf8()
            : FileContent.Binary();
    }

    private static GitConflictContentKind GetOverallKind(
        GitConflictIndexEntry entry,
        FileContent result,
        FileContent yours,
        FileContent theirs)
    {
        FileContent[] required = new[] { result }
            .Concat(entry.YoursObject is null ? [] : [yours])
            .Concat(entry.TheirsObject is null ? [] : [theirs])
            .ToArray();
        if (required.Any(content => content.Kind == GitConflictContentKind.TooLarge))
        {
            return GitConflictContentKind.TooLarge;
        }

        if (required.Any(content => content.Kind == GitConflictContentKind.InvalidUtf8))
        {
            return GitConflictContentKind.InvalidUtf8;
        }

        if (!entry.UsesOnlyRegularFiles
            || required.Any(content => content.Kind is GitConflictContentKind.Binary or GitConflictContentKind.Missing))
        {
            return result.Kind == GitConflictContentKind.Missing && required.All(content =>
                content.Kind is GitConflictContentKind.Text or GitConflictContentKind.Missing)
                ? GitConflictContentKind.Text
                : GitConflictContentKind.Binary;
        }

        return required.All(content => content.Kind == GitConflictContentKind.Text)
            ? GitConflictContentKind.Text
            : GitConflictContentKind.Binary;
    }

    private static GitConflictFileVersion CreateVersion(string fullPath, byte[]? bytes)
    {
        return bytes is null || !File.Exists(fullPath)
            ? new(-1, DateTime.MinValue, string.Empty)
            : new(
                bytes.LongLength,
                File.GetLastWriteTimeUtc(fullPath),
                Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static async Task<GitConflictFileVersion> ReadVersionAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(fullPath))
        {
            return new(-1, DateTime.MinValue, string.Empty);
        }

        FileInfo file = new(fullPath);
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > MaximumConflictBytes)
        {
            return new(file.Length, file.LastWriteTimeUtc, "不可编辑");
        }

        byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return new(bytes.LongLength, file.LastWriteTimeUtc, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandMode mode,
        CancellationToken cancellationToken)
    {
        return _runner.RunAsync(
            _runtime.ExecutablePath!,
            workingDirectory,
            arguments,
            mode,
            cancellationToken);
    }

    private static string Decode(byte[] bytes)
    {
        return DocumentClassifier.DecodeUtf8(bytes);
    }

    private static bool TryResolvePath(
        GitRepositorySnapshot repository,
        string relativePath,
        out string? repositoryRoot,
        out string? normalizedPath,
        out string? fullPath,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(repository);
        repositoryRoot = repository.RepositoryRoot;
        normalizedPath = null;
        fullPath = null;
        error = null;
        if (repository.Kind != GitRepositoryKind.WorkingTree || string.IsNullOrWhiteSpace(repositoryRoot))
        {
            error = "冲突解决只适用于具有工作区的 Git 仓库。";
            return false;
        }

        if (!GitPathValidator.TryNormalizeRelativePath(
                repositoryRoot,
                relativePath,
                out normalizedPath,
                out fullPath))
        {
            error = "冲突文件路径无效或超出仓库范围。";
            return false;
        }

        return true;
    }

    private static GitConflictMutationResult MutationSuccess(GitAdvancedOperationResult result)
    {
        return result.IsSuccess && result.Session is not null
            ? GitConflictMutationResult.Success(result.Session, result.ActualStatus)
            : MutationFailure(result);
    }

    private static GitConflictMutationResult MutationFailure(GitAdvancedOperationResult result)
    {
        return GitConflictMutationResult.Failure(
            result.FailureKind == GitOperationFailureKind.None
                ? GitOperationFailureKind.CommandFailed
                : result.FailureKind,
            result.ErrorMessage ?? "无法读取冲突处理后的仓库状态。",
            result.Session,
            result.ActualStatus);
    }

    private static GitOperationFailureKind NormalizeFailure(GitCommandResult result)
    {
        return result.FailureKind == GitOperationFailureKind.None
            ? GitOperationFailureKind.CommandFailed
            : result.FailureKind;
    }

    private sealed record FileContent(GitConflictContentKind Kind, byte[]? Bytes)
    {
        public static FileContent Text(byte[] bytes) => new(GitConflictContentKind.Text, bytes);

        public static FileContent Binary() => new(GitConflictContentKind.Binary, null);

        public static FileContent InvalidUtf8() => new(GitConflictContentKind.InvalidUtf8, null);

        public static FileContent TooLarge() => new(GitConflictContentKind.TooLarge, null);

        public static FileContent Missing() => new(GitConflictContentKind.Missing, null);

        public static FileContent Failed() => new(GitConflictContentKind.Binary, null);
    }
}
