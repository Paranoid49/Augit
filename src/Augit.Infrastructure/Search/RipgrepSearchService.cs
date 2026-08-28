using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Augit.Core.Search;

namespace Augit.Infrastructure.Search;

public sealed class RipgrepSearchService
{
    private const int MaximumErrorCharacters = 32 * 1024;
    private readonly string _executablePath;

    public RipgrepSearchService(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _executablePath = Path.GetFullPath(executablePath);
    }

    public async Task<IReadOnlyList<FileSearchResult>> SearchFilesAsync(
        string workspaceRoot,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        query ??= string.Empty;

        ProcessStartInfo startInfo = CreateStartInfo(workspaceRoot);
        AddCommonFileArguments(startInfo);

        List<FileSearchResult> matches = [];
        using CancellationTokenSource timeout = new(SearchOptions.Timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        await RunLinesAsync(
            startInfo,
            line =>
            {
                string relativePath = NormalizeRelativePath(line);
                int score = ScoreFile(relativePath, query);
                if (score >= 0)
                {
                    matches.Add(new(relativePath, score));
                }

                return true;
            },
            linked.Token).ConfigureAwait(false);

        return matches
            .OrderBy(result => result.Score)
            .ThenBy(result => result.RelativePath.Length)
            .ThenBy(result => result.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(SearchOptions.MaximumFileResults)
            .ToArray();
    }

    public async Task<TextSearchResult> SearchTextAsync(
        string workspaceRoot,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(options);
        if (options.IsEmpty)
        {
            return new([], false, false, false, null);
        }

        ProcessStartInfo startInfo = CreateStartInfo(workspaceRoot);
        AddTextArguments(startInfo, options);
        List<TextSearchMatch> matches = [];
        bool truncated = false;
        bool timedOut = false;
        bool cancelled = false;
        string? error = null;

        using CancellationTokenSource timeout = new(SearchOptions.Timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            ProcessRunResult runResult = await RunLinesAsync(
                startInfo,
                line =>
                {
                    if (TryParseMatch(line, out TextSearchMatch? match) && match is not null)
                    {
                        matches.Add(match);
                        if (matches.Count > SearchOptions.MaximumTextResults)
                        {
                            truncated = true;
                            return false;
                        }
                    }

                    return true;
                },
                linked.Token).ConfigureAwait(false);
            if (runResult.ExitCode is not 0 and not 1 && !truncated)
            {
                error = string.IsNullOrWhiteSpace(runResult.Error) ? "搜索进程执行失败。" : runResult.Error;
            }
        }
        catch (OperationCanceledException)
        {
            timedOut = timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            cancelled = cancellationToken.IsCancellationRequested;
        }

        if (truncated)
        {
            matches.RemoveRange(SearchOptions.MaximumTextResults, matches.Count - SearchOptions.MaximumTextResults);
        }

        return new(matches, truncated, timedOut, cancelled, timedOut ? "搜索超过 15 秒，已停止。" : error);
    }

    private static void AddCommonFileArguments(ProcessStartInfo startInfo)
    {
        startInfo.ArgumentList.Add("--no-config");
        startInfo.ArgumentList.Add("--files");
        startInfo.ArgumentList.Add("--hidden");
        startInfo.ArgumentList.Add("--no-messages");
        startInfo.ArgumentList.Add("--glob");
        startInfo.ArgumentList.Add("!.git/**");
        startInfo.ArgumentList.Add(".");
    }

    private static void AddTextArguments(ProcessStartInfo startInfo, SearchOptions options)
    {
        startInfo.ArgumentList.Add("--no-config");
        startInfo.ArgumentList.Add("--json");
        startInfo.ArgumentList.Add("--hidden");
        startInfo.ArgumentList.Add("--no-messages");
        startInfo.ArgumentList.Add("--glob");
        startInfo.ArgumentList.Add("!.git/**");
        if (options.IncludeIgnoredFiles)
        {
            startInfo.ArgumentList.Add("--no-ignore");
        }

        if (!options.MatchCase)
        {
            startInfo.ArgumentList.Add("--ignore-case");
        }

        if (options.MatchWholeWord)
        {
            startInfo.ArgumentList.Add("--word-regexp");
        }

        if (!options.UseRegularExpression)
        {
            startInfo.ArgumentList.Add("--fixed-strings");
        }

        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(options.Query);
        startInfo.ArgumentList.Add(".");
    }

    private static ProcessStartInfo CreateStartInfo(string workspaceRoot)
    {
        return new()
        {
            WorkingDirectory = Path.GetFullPath(workspaceRoot),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
    }

    private async Task<ProcessRunResult> RunLinesAsync(
        ProcessStartInfo startInfo,
        Func<string, bool> handleLine,
        CancellationToken cancellationToken)
    {
        startInfo.FileName = _executablePath;
        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动随程序附带的 ripgrep。");
        }

        Task<string> errorTask = ReadBoundedErrorAsync(process.StandardError);
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => KillProcess((Process)state!),
            process);
        bool stoppedEarly = false;
        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (!handleLine(line))
                {
                    stoppedEarly = true;
                    KillProcess(process);
                    break;
                }
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            string error = await errorTask.ConfigureAwait(false);
            return new(stoppedEarly ? 0 : process.ExitCode, error);
        }
        finally
        {
            if (!process.HasExited)
            {
                KillProcess(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static async Task<string> ReadBoundedErrorAsync(StreamReader reader)
    {
        char[] buffer = new char[1024];
        StringBuilder builder = new();
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            int remaining = MaximumErrorCharacters - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, remaining));
            }
        }

        return builder.ToString().Trim();
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
        catch (InvalidOperationException)
        {
        }
    }

    private static int ScoreFile(string relativePath, string query)
    {
        if (query.Length == 0)
        {
            return 10;
        }

        string fileName = Path.GetFileName(relativePath);
        if (fileName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (fileName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (fileName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return relativePath.Contains(query, StringComparison.OrdinalIgnoreCase) ? 3 : -1;
    }

    private static bool TryParseMatch(string jsonLine, out TextSearchMatch? match)
    {
        match = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(jsonLine);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("type", out JsonElement type) || type.GetString() != "match")
            {
                return false;
            }

            JsonElement data = root.GetProperty("data");
            string? path = data.GetProperty("path").GetProperty("text").GetString();
            string? lineText = data.GetProperty("lines").GetProperty("text").GetString();
            int lineNumber = data.GetProperty("line_number").GetInt32();
            JsonElement.ArrayEnumerator submatches = data.GetProperty("submatches").EnumerateArray();
            int column = submatches.MoveNext() ? submatches.Current.GetProperty("start").GetInt32() + 1 : 1;
            if (path is null || lineText is null)
            {
                return false;
            }

            match = new(NormalizeRelativePath(path), lineNumber, column, lineText.TrimEnd('\r', '\n'));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.StartsWith("./", StringComparison.Ordinal) || path.StartsWith(".\\", StringComparison.Ordinal)
            ? path[2..]
            : path;
    }

    private sealed record ProcessRunResult(int ExitCode, string Error);
}
