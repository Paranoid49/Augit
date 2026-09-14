namespace Augit.Core.Git;

public enum GitConflictContentKind
{
    Text,
    Binary,
    InvalidUtf8,
    TooLarge,
    Missing,
}

public enum GitConflictSide
{
    Yours,
    Theirs,
}

public enum GitConflictBlockChoice
{
    Yours,
    Theirs,
    Both,
}

public sealed record GitConflictFileVersion(
    long Length,
    DateTime LastWriteTimeUtc,
    string Sha256);

public sealed record GitConflictBlock(
    int Start,
    int Length,
    string YoursText,
    string? AncestorText,
    string TheirsText)
{
    public string GetResolvedText(GitConflictBlockChoice choice) => choice switch
    {
        GitConflictBlockChoice.Yours => YoursText,
        GitConflictBlockChoice.Theirs => TheirsText,
        GitConflictBlockChoice.Both => string.Concat(YoursText, TheirsText),
        _ => throw new ArgumentOutOfRangeException(nameof(choice)),
    };
}

public sealed record GitConflictDocument(
    string RelativePath,
    GitConflictContentKind ContentKind,
    string YoursLabel,
    string TheirsLabel,
    string? YoursText,
    string? TheirsText,
    string? ResultText,
    IReadOnlyList<GitConflictBlock> Blocks,
    GitConflictFileVersion? FileVersion,
    GitOperationKind Operation);

public sealed record GitConflictLoadResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitConflictDocument? Document)
{
    public static GitConflictLoadResult Success(GitConflictDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(true, GitOperationFailureKind.None, null, document);
    }

    public static GitConflictLoadResult Failure(
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

public sealed record GitConflictSaveRequest(
    string RelativePath,
    string ResultText,
    GitConflictFileVersion ExpectedVersion,
    GitOperationKind ExpectedOperation);

public sealed record GitConflictMutationResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitOperationSession? Session,
    GitStatusSnapshot? ActualStatus)
{
    public static GitConflictMutationResult Success(
        GitOperationSession session,
        GitStatusSnapshot? actualStatus = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new(true, GitOperationFailureKind.None, null, session, actualStatus);
    }

    public static GitConflictMutationResult Failure(
        GitOperationFailureKind failureKind,
        string errorMessage,
        GitOperationSession? session = null,
        GitStatusSnapshot? actualStatus = null)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, session, actualStatus);
    }
}

public static class GitConflictText
{
    public static IReadOnlyList<GitConflictBlock> Parse(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        List<GitConflictBlock> blocks = [];
        int start = -1;
        int contentStart = 0;
        int ancestor = -1;
        int ancestorContent = 0;
        int separator = -1;
        int theirsStart = 0;
        int position = 0;
        int lineCount = 0;
        // 扫描只保存标记边界，不复制普通行；闭合后才提取块内容，未闭合的块不参与解决。
        while (position < text.Length)
        {
            if ((lineCount++ & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            int newline = text.IndexOf('\n', position);
            int end = newline < 0 ? text.Length : newline + 1;
            int contentEnd = newline < 0 ? end : newline;
            if (contentEnd > position && text[contentEnd - 1] == '\r')
            {
                contentEnd--;
            }
            ReadOnlySpan<char> content = text.AsSpan(position, contentEnd - position);
            if (IsMarker(content, "<<<<<<<"))
            {
                start = position;
                contentStart = end;
                ancestor = separator = -1;
            }
            else if (start >= 0)
            {
                if (ancestor < 0 && separator < 0 && IsMarker(content, "|||||||"))
                {
                    ancestor = position;
                    ancestorContent = end;
                }
                else if (separator < 0 && content.SequenceEqual("======="))
                {
                    separator = position;
                    theirsStart = end;
                }
                else if (separator >= 0 && IsMarker(content, ">>>>>>>"))
                {
                    blocks.Add(new(start, end - start,
                        text[contentStart..(ancestor >= 0 ? ancestor : separator)],
                        ancestor >= 0 ? text[ancestorContent..separator] : null,
                        text[theirsStart..position]));
                    start = -1;
                }
            }
            position = end;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return blocks;
    }

    public static bool TryResolveBlock(
        string text,
        int blockIndex,
        GitConflictBlockChoice choice,
        out string? result)
    {
        ArgumentNullException.ThrowIfNull(text);
        result = null;
        IReadOnlyList<GitConflictBlock> blocks = Parse(text);
        if (blockIndex < 0 || blockIndex >= blocks.Count)
        {
            return false;
        }

        GitConflictBlock block = blocks[blockIndex];
        string replacement = block.GetResolvedText(choice);
        result = string.Concat(text.AsSpan(0, block.Start), replacement, text.AsSpan(block.Start + block.Length));
        return true;
    }

    private static bool IsMarker(ReadOnlySpan<char> content, ReadOnlySpan<char> marker)
    {
        return content.StartsWith(marker, StringComparison.Ordinal)
            && (content.Length == marker.Length || content[marker.Length] == ' ');
    }
}
