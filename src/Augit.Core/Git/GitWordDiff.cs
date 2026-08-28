namespace Augit.Core.Git;

public static class GitWordDiff
{
    private const int MaximumTokensPerLine = 256;

    public static (IReadOnlyList<GitTextSpan> OldChanges, IReadOnlyList<GitTextSpan> NewChanges) FindChanges(
        string oldText,
        string newText)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        if (oldText.Equals(newText, StringComparison.Ordinal))
        {
            return ([], []);
        }

        List<Token> oldTokens = Tokenize(oldText);
        List<Token> newTokens = Tokenize(newText);
        if (oldTokens.Count > MaximumTokensPerLine || newTokens.Count > MaximumTokensPerLine)
        {
            return FindMiddleChange(oldText, newText);
        }

        int[,] lengths = new int[oldTokens.Count + 1, newTokens.Count + 1];
        for (int oldIndex = oldTokens.Count - 1; oldIndex >= 0; oldIndex--)
        {
            for (int newIndex = newTokens.Count - 1; newIndex >= 0; newIndex--)
            {
                lengths[oldIndex, newIndex] = oldTokens[oldIndex].Text.Equals(
                    newTokens[newIndex].Text,
                    StringComparison.Ordinal)
                    ? lengths[oldIndex + 1, newIndex + 1] + 1
                    : Math.Max(lengths[oldIndex + 1, newIndex], lengths[oldIndex, newIndex + 1]);
            }
        }

        bool[] oldMatched = new bool[oldTokens.Count];
        bool[] newMatched = new bool[newTokens.Count];
        int left = 0;
        int right = 0;
        while (left < oldTokens.Count && right < newTokens.Count)
        {
            if (oldTokens[left].Text.Equals(newTokens[right].Text, StringComparison.Ordinal))
            {
                oldMatched[left++] = true;
                newMatched[right++] = true;
            }
            else if (lengths[left + 1, right] >= lengths[left, right + 1])
            {
                left++;
            }
            else
            {
                right++;
            }
        }

        return (BuildSpans(oldTokens, oldMatched), BuildSpans(newTokens, newMatched));
    }

    private static List<Token> Tokenize(string text)
    {
        List<Token> tokens = [];
        int index = 0;
        while (index < text.Length)
        {
            int start = index;
            char current = text[index];
            if (char.IsWhiteSpace(current))
            {
                while (++index < text.Length && char.IsWhiteSpace(text[index]))
                {
                }
            }
            else if (IsCjk(current))
            {
                index++;
            }
            else if (char.IsLetterOrDigit(current) || current == '_')
            {
                while (++index < text.Length
                    && !IsCjk(text[index])
                    && (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
                {
                }
            }
            else
            {
                index += char.IsHighSurrogate(current)
                    && index + 1 < text.Length
                    && char.IsLowSurrogate(text[index + 1])
                    ? 2
                    : 1;
            }

            tokens.Add(new(text[start..index], start, index - start));
        }

        return tokens;
    }

    private static List<GitTextSpan> BuildSpans(IReadOnlyList<Token> tokens, bool[] matched)
    {
        List<GitTextSpan> spans = [];
        for (int index = 0; index < tokens.Count; index++)
        {
            if (matched[index])
            {
                continue;
            }

            Token token = tokens[index];
            if (spans.Count > 0 && spans[^1].Start + spans[^1].Length == token.Start)
            {
                GitTextSpan previous = spans[^1];
                spans[^1] = previous with { Length = previous.Length + token.Length };
            }
            else
            {
                spans.Add(new(token.Start, token.Length));
            }
        }

        return spans;
    }

    private static (IReadOnlyList<GitTextSpan>, IReadOnlyList<GitTextSpan>) FindMiddleChange(
        string oldText,
        string newText)
    {
        int prefix = 0;
        while (prefix < oldText.Length
            && prefix < newText.Length
            && oldText[prefix] == newText[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < oldText.Length - prefix
            && suffix < newText.Length - prefix
            && oldText[oldText.Length - suffix - 1] == newText[newText.Length - suffix - 1])
        {
            suffix++;
        }

        int oldLength = oldText.Length - prefix - suffix;
        int newLength = newText.Length - prefix - suffix;
        return (
            oldLength == 0 ? [] : [new GitTextSpan(prefix, oldLength)],
            newLength == 0 ? [] : [new GitTextSpan(prefix, newLength)]);
    }

    private static bool IsCjk(char value)
    {
        return value is >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF';
    }

    private sealed record Token(string Text, int Start, int Length);
}
