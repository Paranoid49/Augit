using System.Diagnostics;
using Augit.Core.Search;
using Augit.Infrastructure.Search;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class RipgrepSearchServiceTests
{
    private static string RipgrepPath => Path.Combine(AppContext.BaseDirectory, "tools", "rg.exe");

    [TestMethod]
    public async Task 文件搜索遵循Gitignore且隐藏Git目录()
    {
        using TemporaryDirectory temporary = CreateSearchWorkspace();
        RipgrepSearchService service = new(RipgrepPath);

        IReadOnlyList<FileSearchResult> results = await service.SearchFilesAsync(temporary.FullPath, "target");

        Assert.HasCount(1, results);
        Assert.AreEqual("visible-target.txt", results[0].RelativePath.Replace('\\', '/'));
    }

    [TestMethod]
    public async Task 全仓搜索可包含忽略文件但永远排除Git目录()
    {
        using TemporaryDirectory temporary = CreateSearchWorkspace();
        RipgrepSearchService service = new(RipgrepPath);

        TextSearchResult normal = await service.SearchTextAsync(temporary.FullPath, new("needle"));
        TextSearchResult included = await service.SearchTextAsync(temporary.FullPath, new("needle", IncludeIgnoredFiles: true));

        Assert.HasCount(1, normal.Matches);
        Assert.HasCount(2, included.Matches);
        Assert.IsFalse(included.Matches.Any(match => match.RelativePath.Contains(".git", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task 文件搜索最多返回三十项()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.GetPath(".git"));
        for (int index = 0; index < 150; index++)
        {
            File.WriteAllText(temporary.GetPath($"target-{index:D3}.txt"), string.Empty);
        }

        IReadOnlyList<FileSearchResult> results = await new RipgrepSearchService(RipgrepPath)
            .SearchFilesAsync(temporary.FullPath, "target");

        Assert.HasCount(SearchOptions.MaximumFileResults, results);
        CollectionAssert.AreEqual(Enumerable.Range(0, 30).Select(index => $"target-{index:D3}.txt").ToArray(),
            results.Select(result => result.RelativePath).ToArray());
    }

    [TestMethod]
    public async Task 文件名搜索只匹配文件名且保留最佳三十项顺序()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.GetPath("target-directory"));
        File.WriteAllText(temporary.GetPath("target-directory/unrelated.txt"), string.Empty);
        List<string> names = ["target", "target.txt", "target.cs", "prefix-target.txt"];
        names.AddRange(Enumerable.Range(0, 180).Select(index => $"target-{index:D3}.txt"));
        foreach (string name in names.AsEnumerable().Reverse()) File.WriteAllText(temporary.GetPath(name), string.Empty);
        IReadOnlyList<FileSearchResult> actual = await new RipgrepSearchService(RipgrepPath).SearchFilesAsync(temporary.FullPath, "target");
        string[] expected = names.OrderBy(name => name == "target" ? 0 : name.StartsWith("target", StringComparison.Ordinal) ? 1 : 2)
            .ThenBy(name => name.Length).ThenBy(name => name, StringComparer.OrdinalIgnoreCase).Take(30).ToArray();
        CollectionAssert.AreEqual(expected, actual.Select(result => result.RelativePath).ToArray());
        Assert.IsFalse(actual.Any(result => result.RelativePath.Contains("unrelated", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task 预先取消的搜索不启动缺失进程且返回明确取消状态()
    {
        using TemporaryDirectory temporary = new();
        RipgrepSearchService service = new(temporary.GetPath("missing-rg.exe"));
        CancellationToken token = new(canceled: true);
        FileSearchResultSet files = await service.SearchFilesWithStatusAsync(temporary.FullPath, "target", token);
        Assert.IsTrue(files.IsCancelled);
        Assert.AreEqual("搜索已取消。", files.Notice);
        Assert.HasCount(0, files.Matches);
        TextSearchResult text = await service.SearchTextAsync(temporary.FullPath, new("target"), token);
        Assert.IsTrue(text.IsCancelled);
        Assert.HasCount(0, text.Matches);
    }

    [TestMethod]
    public async Task 第一千零一条文本结果触发统一截断提示()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.GetPath(".git"));
        await File.WriteAllLinesAsync(temporary.GetPath("many.txt"), Enumerable.Repeat("needle", 1001));

        TextSearchResult result = await new RipgrepSearchService(RipgrepPath)
            .SearchTextAsync(temporary.FullPath, new("needle"));

        Assert.HasCount(SearchOptions.MaximumTextResults, result.Matches);
        Assert.IsTrue(result.IsTruncated);
        Assert.AreEqual(SearchOptions.ResultsTruncatedMessage, result.Notice);
    }

    /// <summary>
    /// 规格 §7.15 第八条：超时和取消后要**结束 ripgrep 进程**。
    /// </summary>
    /// <remarks>
    /// 真实 ripgrep 往往在毫秒级跑完，无法稳定地在"进行中"取消它，因此这里注入一个会阻塞的命令，
    /// 走 `RipgrepSearchService` **同一套**启动/读行/收尾逻辑：
    /// ① 取消（外部令牌）与② 超时（无外部取消、只有时限）两条通道各跑一次；
    /// 每次都断言进程已经结束，并在 2.5 秒后确认**子进程树**也没跑完（未写出 marker 文件）——
    /// 只 `Kill()` 顶层进程而不结束子进程树时，`ping` 会在后台跑完并写出 marker。
    /// </remarks>
    [TestMethod]
    public async Task 取消或超时结束进行中的搜索时进程与子进程树都被结束()
    {
        using TemporaryDirectory temporary = new();
        string marker = temporary.GetPath("finished.txt");
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add($"ping -n 3 127.0.0.1 >nul & echo done> \"{marker}\"");
        int? cancelledPid = null;
        using (CancellationTokenSource cancel = new())
        {
            cancel.CancelAfter(TimeSpan.FromMilliseconds(300));
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                RipgrepSearchService.RunConfiguredAsync(startInfo, _ => true, cancel.Token, pid => cancelledPid = pid));
        }

        Assert.IsNotNull(cancelledPid, "注入的阻塞进程必须真的启动过（否则这条断言是空的）");
        Assert.IsFalse(IsProcessAlive(cancelledPid.Value), "取消后进程必须已经结束");

        int? timedOutPid = null;
        using (CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(300)))
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                RipgrepSearchService.RunConfiguredAsync(startInfo, _ => true, timeout.Token, pid => timedOutPid = pid));
        }

        Assert.IsNotNull(timedOutPid, "超时通道同样必须真的启动过进程");
        Assert.IsFalse(IsProcessAlive(timedOutPid.Value), "超时后进程必须已经结束");

        // 子进程树：两次都只跑了 ~300ms，`ping -n 3` 需要约 2 秒；若子进程没被一起结束，
        // 它会在后台跑完并写出 marker。
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        Assert.IsFalse(File.Exists(marker), "取消/超时后子进程树也必须被结束（marker 不应出现）");
    }

    private static bool IsProcessAlive(int processId)
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

    private static TemporaryDirectory CreateSearchWorkspace()
    {
        TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.GetPath(".git"));
        File.WriteAllText(temporary.GetPath(".gitignore"), "ignored-target.txt\n");
        File.WriteAllText(temporary.GetPath("visible-target.txt"), "needle\n");
        File.WriteAllText(temporary.GetPath("ignored-target.txt"), "needle\n");
        File.WriteAllText(temporary.GetPath(".git/secret-target.txt"), "needle\n");
        return temporary;
    }
}
