using Augit.Core.Git;

namespace Augit.App.VisualAuditHost;

/// <summary>
/// 只在审计宿主中控制第一次历史文件查询，其他读取仍委托真实 Git。
/// 不改变仓库、系统配置或正式应用的查询行为。
/// </summary>
internal sealed class HistoryDiffAuditService(IGitHistoryService inner, bool failFirst) : IGitHistoryService
{
    internal const string FailureMessage = "无法读取提交中的文件：Git 查询失败。";
    private bool _first = true;

    public async Task<GitComparisonResult> ReadCommitFileDiffAsync(GitRepositorySnapshot repository,
        string revision, string relativePath, bool ignoreWhitespace = false, CancellationToken cancellationToken = default)
    {
        if (_first)
        {
            _first = false;
            // 审计宿主的加载态只需足够长来采集稳定帧；有限时长也避免无截图手动审计永久挂起。
            await Task.Delay(failFirst ? 500 : 30000, cancellationToken);
            if (failFirst)
            {
                return GitComparisonResult.Failure(GitOperationFailureKind.CommandFailed, FailureMessage);
            }
        }
        return await inner.ReadCommitFileDiffAsync(repository, revision, relativePath, ignoreWhitespace, cancellationToken);
    }

    public Task<GitHistoryResult> ReadPageAsync(GitRepositorySnapshot repository,
        GitHistoryRequest request, CancellationToken cancellationToken = default) =>
        inner.ReadPageAsync(repository, request, cancellationToken);

    public Task<GitCommitDetailsResult> ReadCommitAsync(GitRepositorySnapshot repository,
        string revision, CancellationToken cancellationToken = default) =>
        inner.ReadCommitAsync(repository, revision, cancellationToken);

    public Task<GitBlameResult> ReadBlameAsync(GitRepositorySnapshot repository,
        string relativePath, string? revision = null, CancellationToken cancellationToken = default) =>
        inner.ReadBlameAsync(repository, relativePath, revision, cancellationToken);

    public Task<GitComparisonResult> CompareAsync(GitRepositorySnapshot repository,
        GitComparisonRequest request, CancellationToken cancellationToken = default) =>
        inner.CompareAsync(repository, request, cancellationToken);
}
