using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Augit.Infrastructure.Settings;
using Augit.Infrastructure.Terminal;

namespace Augit.Terminal.TestHost;

internal static partial class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        if (arguments.Length != 2)
        {
            return 2;
        }

        string resultPath = Path.GetFullPath(arguments[0]);
        string workingDirectory = Path.GetFullPath(arguments[1]);
        try
        {
            TerminalLaunchResult launchResult = TerminalShellResolver.Resolve(
                new() { TerminalShell = TerminalShellIds.CommandPrompt },
                workingDirectory);
            if (!launchResult.IsSuccess)
            {
                throw new InvalidOperationException(launchResult.ErrorMessage);
            }

            StringBuilder output = new();
            TaskCompletionSource<bool> marker = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<int> childProcess = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using ConPtyTerminalSession session = ConPtyTerminalSession.Start(
                launchResult.LaunchInfo!,
                workingDirectory,
                100,
                30);
            session.OutputReceived += (_, text) =>
            {
                lock (output)
                {
                    output.Append(text);
                    string current = output.ToString();
                    if (current.Contains("AUGIT_CONPTY_OK", StringComparison.Ordinal))
                    {
                        marker.TrySetResult(true);
                    }

                    Match childMatch = ChildProcessPattern().Match(current);
                    if (childMatch.Success && int.TryParse(childMatch.Groups[1].Value, out int processId))
                    {
                        childProcess.TrySetResult(processId);
                    }
                }
            };

            await session.WriteAsync("echo AUGIT_CONPTY_OK\r\n");
            await marker.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await session.WriteAsync(
                "powershell.exe -NoProfile -Command \"[Console]::WriteLine('AUGIT_CHILD_PID=' + $PID); Start-Sleep -Seconds 60\"\r\n");
            int childProcessId = await childProcess.Task.WaitAsync(TimeSpan.FromSeconds(10));
            DateTime foregroundDeadline = DateTime.UtcNow.AddSeconds(5);
            while (!session.HasForegroundProcess && DateTime.UtcNow < foregroundDeadline)
            {
                await Task.Delay(25);
            }

            bool foregroundDetected = session.HasForegroundProcess;
            await session.StopAsync();
            DateTime exitDeadline = DateTime.UtcNow.AddSeconds(5);
            while (IsRunning(childProcessId) && DateTime.UtcNow < exitDeadline)
            {
                await Task.Delay(25);
            }

            TerminalTestResult result = new(
                foregroundDetected && !session.IsRunning && !IsRunning(childProcessId),
                foregroundDetected,
                !session.IsRunning,
                !IsRunning(childProcessId),
                null);
            await WriteResultAsync(resultPath, result);
            return result.IsSuccess ? 0 : 1;
        }
        catch (Exception exception)
        {
            await WriteResultAsync(resultPath, new(false, false, false, false, exception.ToString()));
            return 1;
        }
    }

    private static Task WriteResultAsync(string path, TerminalTestResult result)
    {
        return File.WriteAllTextAsync(path, JsonSerializer.Serialize(result));
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [GeneratedRegex("AUGIT_CHILD_PID=(\\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ChildProcessPattern();

    private sealed record TerminalTestResult(
        bool IsSuccess,
        bool ForegroundDetected,
        bool ShellExited,
        bool ChildExited,
        string? ErrorMessage);
}
