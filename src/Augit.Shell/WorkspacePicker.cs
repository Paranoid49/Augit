using Augit.Infrastructure.Files;

namespace Augit.Shell;

internal enum WorkspacePickOutcome
{
    /// <summary>用户取消（或选择框无法弹出）：什么都不做，界面保持原状。</summary>
    Cancelled,

    /// <summary>选到了目录。</summary>
    Picked,

    /// <summary>选到的东西不可用（例如不是本地目录）。</summary>
    Invalid,
}

internal sealed record WorkspacePickResult(WorkspacePickOutcome Outcome, string? Path, string? Reason);

/// <summary>
/// 「选择目录…」的决策：系统对话框只负责给路径，取值是否可用由这里判定并给出原因。
/// 对话框本身可注入，因此取消 / 选到无效目录 / 选到可用目录三条路径都能在测试里判住
/// （真弹出系统对话框无法在无人值守环境验证）。
/// </summary>
internal static class WorkspacePicker
{
    public static WorkspacePickResult Pick(Func<string, string?> dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        string? picked = dialog("选择工作区目录");
        if (string.IsNullOrWhiteSpace(picked))
        {
            return new(WorkspacePickOutcome.Cancelled, null, null);
        }

        if (!WorkspaceDirectoryService.ValidateRoot(picked).IsValid)
        {
            return new(WorkspacePickOutcome.Invalid, null, "所选目录不存在或不是可访问的本地目录。");
        }

        // 对话框可能带回结尾分隔符：去掉它，界面与 workspace/open 都拿到同一个规范路径。
        string normalized = Path.GetFullPath(picked).TrimEnd(Path.DirectorySeparatorChar);
        return new(WorkspacePickOutcome.Picked, normalized.Length > 0 ? normalized : Path.GetFullPath(picked), null);
    }
}
