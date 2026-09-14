namespace Augit.Core.Git;

public sealed record GitRemoteInfo(string Name, string FetchUrl, string PushUrl);

public enum GitPullMode
{
    RepositoryConfigured,
    Merge,
    Rebase,
    FastForwardOnly,
}

public sealed record GitRemoteListResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    IReadOnlyList<GitRemoteInfo>? Remotes)
{
    public static GitRemoteListResult Success(IReadOnlyList<GitRemoteInfo> remotes)
    {
        ArgumentNullException.ThrowIfNull(remotes);
        return new(true, GitOperationFailureKind.None, null, remotes);
    }

    public static GitRemoteListResult Failure(GitOperationFailureKind failureKind, string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null);
    }
}

public sealed record GitRemoteOperationResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    IReadOnlyList<GitRemoteInfo>? ActualRemotes,
    GitStatusSnapshot? ActualStatus)
{
    public static GitRemoteOperationResult Success(
        IReadOnlyList<GitRemoteInfo>? actualRemotes,
        GitStatusSnapshot? actualStatus)
    {
        return new(true, GitOperationFailureKind.None, null, actualRemotes, actualStatus);
    }

    public static GitRemoteOperationResult Failure(
        GitOperationFailureKind failureKind,
        string errorMessage,
        IReadOnlyList<GitRemoteInfo>? actualRemotes = null,
        GitStatusSnapshot? actualStatus = null)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, actualRemotes, actualStatus);
    }
}

public sealed record GitPushCommitPreview(string Hash, string Subject);

public sealed record GitPushPreview(
    string LocalReference,
    string RemoteName,
    string RemoteReference,
    IReadOnlyList<GitPushCommitPreview> Commits);

public sealed record GitPushPreviewResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitPushPreview? Preview,
    bool CanDefineRemote,
    string? LocalReference)
{
    public static GitPushPreviewResult Success(GitPushPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new(true, GitOperationFailureKind.None, null, preview, false, preview.LocalReference);
    }

    public static GitPushPreviewResult Failure(
        GitOperationFailureKind failureKind,
        string errorMessage,
        bool canDefineRemote = false,
        string? localReference = null)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null, canDefineRemote, localReference);
    }
}
