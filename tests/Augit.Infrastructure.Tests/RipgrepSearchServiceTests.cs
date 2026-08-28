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
    public async Task 文件搜索最多返回一百项()
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
