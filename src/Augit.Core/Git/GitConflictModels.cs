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
    string TheirsText);

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
    public static IReadOnlyList<GitConflictBlock> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<LineInfo> lines = ReadLines(text);
        List<GitConflictBlock> blocks = [];
        for (int index = 0; index < lines.Count; index++)
        {
            if (!IsMarker(lines[index].Content, "<<<<<<<"))
            {
                continue;
            }

            LineInfo start = lines[index];
            LineInfo? ancestor = null;
            LineInfo? separator = null;
            LineInfo? end = null;
            for (int inner = index + 1; inner < lines.Count; inner++)
            {
                LineInfo line = lines[inner];
                if (ancestor is null && separator is null && IsMarker(line.Content, "|||||||"))
                {
                    ancestor = line;
                    continue;
                }

                if (separator is null && line.Content.Equals("=======", StringComparison.Ordinal))
                {
                    separator = line;
                    continue;
                }

                if (separator is not null && IsMarker(line.Content, ">>>>>>>"))
                {
                    end = line;
                    index = inner;
                    break;
                }

                if (IsMarker(line.Content, "<<<<<<<"))
                {
                    break;
                }
            }

            if (separator is null || end is null)
            {
                continue;
            }

            int yoursEnd = (ancestor ?? separator).Start;
            string yours = text[start.End..yoursEnd];
            string? ancestorText = ancestor is null
                ? null
                : text[ancestor.End..separator.Start];
            string theirs = text[separator.End..end.Start];
            blocks.Add(new(
                start.Start,
                end.End - start.Start,
                yours,
                ancestorText,
                theirs));
        }

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
        string replacement = choice switch
        {
            GitConflictBlockChoice.Yours => block.YoursText,
            GitConflictBlockChoice.Theirs => block.TheirsText,
            GitConflictBlockChoice.Both => string.Concat(block.YoursText, block.TheirsText),
            _ => throw new ArgumentOutOfRangeException(nameof(choice)),
        };
        result = string.Concat(text.AsSpan(0, block.Start), replacement, text.AsSpan(block.Start + block.Length));
        return true;
    }

    private static List<LineInfo> ReadLines(string text)
    {
        List<LineInfo> lines = [];
        int start = 0;
        while (start < text.Length)
        {
            int newline = text.IndexOf('\n', start);
            int end = newline < 0 ? text.Length : newline + 1;
            int contentEnd = newline < 0 ? end : newline;
            if (contentEnd > start && text[contentEnd - 1] == '\r')
            {
                contentEnd--;
            }

            lines.Add(new(start, end, text[start..contentEnd]));
            start = end;
        }

        return lines;
    }

    private static bool IsMarker(string content, string marker)
    {
        return content.Equals(marker, StringComparison.Ordinal)
            || content.StartsWith(string.Concat(marker, " "), StringComparison.Ordinal);
    }

    private sealed record LineInfo(int Start, int End, string Content);
}
