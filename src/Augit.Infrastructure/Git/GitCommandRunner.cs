using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

internal sealed class GitCommandRunner
{
    private const int DefaultMaximumOutputBytes = 1024 * 1024;
    private static readonly TimeSpan DefaultQueryTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _queryTimeout;
    private readonly int _maximumOutputBytes;

    public GitCommandRunner()
        : this(DefaultQueryTimeout, DefaultMaximumOutputBytes)
    {
    }

    internal GitCommandRunner(TimeSpan queryTimeout, int maximumOutputBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(queryTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOutputBytes);

        _queryTimeout = queryTimeout;
        _maximumOutputBytes = maximumOutputBytes;
    }

    public Task<GitCommandResult> RunAsync(
        string executablePath,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandMode mode,
        CancellationToken cancellationToken = default)
    {
        return RunCoreAsync(
            executablePath,
            workingDirectory,
            arguments,
            mode,
            null,
            null,
            false,
            cancellationToken);
    }

    public Task<GitCommandResult> RunWithAdditionalSuccessExitCodesAsync(
        string executablePath,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandMode mode,
        IReadOnlySet<int> additionalSuccessExitCodes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(additionalSuccessExitCodes);
        return RunCoreAsync(
            executablePath,
            workingDirectory,
            arguments,
            mode,
            additionalSuccessExitCodes,
            null,
            false,
            cancellationToken);
    }

    public Task<GitCommandResult> RunWithStandardInputAsync(
        string executablePath,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandMode mode,
        string standardInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(standardInput);
        return RunCoreAsync(
            executablePath,
            workingDirectory,
            arguments,
            mode,
            null,
            standardInput,
            false,
            cancellationToken);
    }

    public Task<GitCommandResult> RunWithRawOutputAsync(
        string executablePath,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandMode mode,
        CancellationToken cancellationToken = default)
    {
        return RunCoreAsync(
            executablePath,
            workingDirectory,
            arguments,
            mode,
            null,
            null,
            true,
            cancellationToken);
    }

    private async Task<GitCommandResult> RunCoreAsync(
        string executablePath,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandMode mode,
        IReadOnlySet<int>? additionalSuccessExitCodes,
        string? standardInput,
        bool captureRawOutput,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetFullPath(workingDirectory),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "Never";
        startInfo.Environment["SSH_ASKPASS_REQUIRE"] = "never";
        if (mode == GitCommandMode.LocalQuery)
        {
            startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        }

        using Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return FailedToStart("系统未能启动 Git。");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return FailedToStart("无法启动 Git，请检查可执行文件路径和权限。");
        }

        Task<BoundedOutput> outputTask = ReadBoundedAsync(process.StandardOutput.BaseStream, captureRawOutput);
        Task<BoundedOutput> errorTask = ReadBoundedAsync(process.StandardError.BaseStream, false);
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
        }
        finally
        {
            process.StandardInput.Close();
        }

        using CancellationTokenSource? timeout = mode == GitCommandMode.LocalQuery
            ? new CancellationTokenSource(_queryTimeout)
            : null;
        using CancellationTokenSource linked = timeout is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using CancellationTokenRegistration cancellationRegistration = linked.Token.Register(
            static state => KillProcessAndWait((Process)state!),
            process);

        bool waitWasCancelled = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            waitWasCancelled = true;
            KillProcess(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (!process.HasExited)
            {
                KillProcess(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        // 取消回调可能先结束进程，使 WaitForExitAsync 正常完成，因此必须在读取退出码前固定取消状态。
        bool cancellationWasRequested = waitWasCancelled || linked.IsCancellationRequested;
        if (cancellationWasRequested)
        {
            BoundedOutput cancelledOutput = await outputTask.ConfigureAwait(false);
            BoundedOutput cancelledError = await errorTask.ConfigureAwait(false);
            bool timedOut = timeout?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested;
            return new(
                null,
                cancelledOutput.Text,
                timedOut ? "Git 查询超过 30 秒，已停止。" : "Git 操作已取消。",
                timedOut ? GitOperationFailureKind.TimedOut : GitOperationFailureKind.Cancelled,
                cancelledOutput.IsTruncated,
                cancelledError.IsTruncated,
                cancelledOutput.Bytes);
        }

        BoundedOutput rawOutput = await outputTask.ConfigureAwait(false);
        BoundedOutput rawError = await errorTask.ConfigureAwait(false);
        string output = rawOutput.Text;
        string error = GitOutputSanitizer.Sanitize(rawError.Text);
        if (rawError.IsTruncated)
        {
            error = string.Concat(error, Environment.NewLine, "Git 错误输出超过上限，已截断。").Trim();
        }

        if (process.ExitCode == 0 || additionalSuccessExitCodes?.Contains(process.ExitCode) == true)
        {
            return new(
                process.ExitCode,
                output,
                string.Empty,
                GitOperationFailureKind.None,
                rawOutput.IsTruncated,
                rawError.IsTruncated,
                rawOutput.Bytes);
        }

        string failureOutput = string.IsNullOrWhiteSpace(error)
            ? GitOutputSanitizer.Sanitize(output)
            : error;
        if (rawOutput.IsTruncated && string.IsNullOrWhiteSpace(error))
        {
            failureOutput = string.Concat(
                failureOutput,
                Environment.NewLine,
                "Git 输出超过上限，已截断。").Trim();
        }
        GitOperationFailureKind failureKind = RequiresInteractiveInput(failureOutput)
            ? GitOperationFailureKind.InteractiveInputRequired
            : GitOperationFailureKind.CommandFailed;
        string message = failureKind == GitOperationFailureKind.InteractiveInputRequired
            ? "Git 需要交互输入，图形操作已停止。"
            : string.IsNullOrWhiteSpace(failureOutput) ? "Git 命令执行失败。" : failureOutput;
        return new(
            process.ExitCode,
            output,
            message,
            failureKind,
            rawOutput.IsTruncated,
            rawError.IsTruncated,
            rawOutput.Bytes);
    }

    private async Task<BoundedOutput> ReadBoundedAsync(Stream stream, bool captureBytes)
    {
        byte[] buffer = new byte[8192];
        using MemoryStream stored = new(Math.Min(_maximumOutputBytes, 16 * 1024));
        bool isTruncated = false;
        int read;
        while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            int remaining = _maximumOutputBytes - checked((int)stored.Length);
            if (remaining > 0)
            {
                int bytesToStore = Math.Min(read, remaining);
                stored.Write(buffer, 0, bytesToStore);
                isTruncated |= bytesToStore < read;
            }
            else
            {
                isTruncated = true;
            }
        }

        return new(
            Encoding.UTF8.GetString(stored.GetBuffer(), 0, checked((int)stored.Length)),
            isTruncated,
            captureBytes ? stored.ToArray() : null);
    }

    private static bool RequiresInteractiveInput(string output)
    {
        return output.Contains("terminal prompts disabled", StringComparison.OrdinalIgnoreCase)
            || output.Contains("could not read Username", StringComparison.OrdinalIgnoreCase)
            || output.Contains("could not read Password", StringComparison.OrdinalIgnoreCase)
            || output.Contains("user interactivity has been disabled", StringComparison.OrdinalIgnoreCase)
            || output.Contains("authentication prompts disabled", StringComparison.OrdinalIgnoreCase);
    }

    private static GitCommandResult FailedToStart(string message)
    {
        return new(null, string.Empty, message, GitOperationFailureKind.CommandFailed);
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
    }

    private static void KillProcessAndWait(Process process)
    {
        KillProcess(process);
        try
        {
            process.WaitForExit();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed record BoundedOutput(string Text, bool IsTruncated, byte[]? Bytes);
}
