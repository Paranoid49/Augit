namespace Augit.Core.Git;

public enum GitCommitMessageSource
{
    UserInput,
    GitGenerated,
}

public sealed record GitCommitRequest(
    IReadOnlyCollection<string> SelectedRelativePaths,
    string Message,
    bool Amend = false,
    GitCommitMessageSource MessageSource = GitCommitMessageSource.UserInput);

public sealed record GitCommitPolicy(
    bool HasCommitMessageHook,
    bool HasCommitlintConfiguration)
{
    public bool UsesRepositoryRules => HasCommitMessageHook || HasCommitlintConfiguration;
}

public sealed record GitCommitPolicyResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitCommitPolicy? Policy)
{
    public static GitCommitPolicyResult Success(GitCommitPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new(true, GitOperationFailureKind.None, null, policy);
    }

    public static GitCommitPolicyResult Failure(GitOperationFailureKind failureKind, string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null);
    }
}

public sealed record GitCommitMessageResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    string? Message)
{
    public static GitCommitMessageResult Success(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new(true, GitOperationFailureKind.None, null, message);
    }

    public static GitCommitMessageResult Failure(
        GitOperationFailureKind failureKind,
        string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null);
    }
}

public sealed record GitCommitResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    string? CommitHash,
    GitStatusSnapshot? ActualStatus)
{
    public static GitCommitResult Success(string commitHash, GitStatusSnapshot actualStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitHash);
        ArgumentNullException.ThrowIfNull(actualStatus);
        return new(true, GitOperationFailureKind.None, null, commitHash, actualStatus);
    }

    public static GitCommitResult Failure(
        GitOperationFailureKind failureKind,
        string errorMessage,
        GitStatusSnapshot? actualStatus = null)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null, actualStatus);
    }
}
