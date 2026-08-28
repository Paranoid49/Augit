using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

internal enum GitCommandMode
{
    LocalQuery,
    LocalWrite,
    Network,
}

internal sealed record GitCommandResult(
    int? ExitCode,
    string StandardOutput,
    string ErrorMessage,
    GitOperationFailureKind FailureKind,
    bool IsOutputTruncated = false,
    bool IsErrorTruncated = false,
    byte[]? RawStandardOutput = null)
{
    public bool IsSuccess => ExitCode is not null && FailureKind == GitOperationFailureKind.None;
}
