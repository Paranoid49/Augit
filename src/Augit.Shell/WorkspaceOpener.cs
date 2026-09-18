using Augit.Infrastructure.Files;

namespace Augit.Shell;

/// <summary>打开工作区的结果。</summary>
internal enum WorkspaceOpenOutcome
{
    /// <summary>请求的目录就是当前窗口的工作区。</summary>
    Current,

    /// <summary>该目录已有 Augit 窗口，已激活它。</summary>
    Activated,

    /// <summary>已为该目录启动新的 Augit 窗口。</summary>
    Launched,

    /// <summary>请求无效或无法打开（原因见 <see cref="WorkspaceOpenResult.Reason"/>）。</summary>
    Invalid,
}

internal sealed record WorkspaceOpenResult(WorkspaceOpenOutcome Outcome, string? Reason);

/// <summary>
/// 「打开工作区」的决策（产品规格 §2：同一目录已经打开时激活原窗口；
/// 单窗口不管理多个仓库，因此不同目录用新窗口）。
///
/// 决策与副作用分开：激活与启动由调用方注入，这样在没有窗口和进程的测试里
/// 也能把四种结果都覆盖到（真启动进程会污染开发机，不适合放进测试）。
/// </summary>
internal static class WorkspaceOpener
{
    public static WorkspaceOpenResult Open(
        string currentWorkspaceRoot,
        string requestedPath,
        Func<string, bool> tryActivate,
        Func<string, bool> launch)
    {
        if (!WorkspaceDirectoryService.ValidateRoot(requestedPath).IsValid)
        {
            return new(WorkspaceOpenOutcome.Invalid, "目录不存在或不是可访问的本地目录。");
        }

        string full = Path.GetFullPath(requestedPath).TrimEnd(Path.DirectorySeparatorChar);
        string current = Path.GetFullPath(currentWorkspaceRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(full, current, StringComparison.OrdinalIgnoreCase))
        {
            return new(WorkspaceOpenOutcome.Current, null);
        }

        if (tryActivate(full))
        {
            return new(WorkspaceOpenOutcome.Activated, null);
        }

        return launch(full)
            ? new(WorkspaceOpenOutcome.Launched, null)
            : new(WorkspaceOpenOutcome.Invalid, "无法为该目录启动新窗口。");
    }
}
