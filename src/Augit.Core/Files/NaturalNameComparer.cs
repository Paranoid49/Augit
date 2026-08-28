using System.Globalization;

namespace Augit.Core.Files;

public sealed class NaturalNameComparer : IComparer<string>
{
    public static NaturalNameComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int leftIndex = 0;
        int rightIndex = 0;
        while (leftIndex < x.Length && rightIndex < y.Length)
        {
            if (char.IsDigit(x[leftIndex]) && char.IsDigit(y[rightIndex]))
            {
                int numberComparison = CompareNumber(x, ref leftIndex, y, ref rightIndex);
                if (numberComparison != 0)
                {
                    return numberComparison;
                }

                continue;
            }

            int leftLength = TextRunLength(x, leftIndex);
            int rightLength = TextRunLength(y, rightIndex);
            int textComparison = CultureInfo.CurrentCulture.CompareInfo.Compare(
                x.AsSpan(leftIndex, leftLength),
                y.AsSpan(rightIndex, rightLength),
                CompareOptions.IgnoreCase);
            if (textComparison != 0)
            {
                return textComparison;
            }

            leftIndex += leftLength;
            rightIndex += rightLength;
        }

        int lengthComparison = (x.Length - leftIndex).CompareTo(y.Length - rightIndex);
        return lengthComparison != 0 ? lengthComparison : StringComparer.Ordinal.Compare(x, y);
    }

    private static int CompareNumber(string left, ref int leftIndex, string right, ref int rightIndex)
    {
        int leftStart = leftIndex;
        int rightStart = rightIndex;
        while (leftIndex < left.Length && char.IsDigit(left[leftIndex]))
        {
            leftIndex++;
        }

        while (rightIndex < right.Length && char.IsDigit(right[rightIndex]))
        {
            rightIndex++;
        }

        ReadOnlySpan<char> leftNumber = left.AsSpan(leftStart, leftIndex - leftStart).TrimStart('0');
        ReadOnlySpan<char> rightNumber = right.AsSpan(rightStart, rightIndex - rightStart).TrimStart('0');
        int significantLengthComparison = leftNumber.Length.CompareTo(rightNumber.Length);
        if (significantLengthComparison != 0)
        {
            return significantLengthComparison;
        }

        int digitsComparison = leftNumber.CompareTo(rightNumber, StringComparison.Ordinal);
        if (digitsComparison != 0)
        {
            return digitsComparison;
        }

        return (leftIndex - leftStart).CompareTo(rightIndex - rightStart);
    }

    private static int TextRunLength(string value, int start)
    {
        int index = start;
        while (index < value.Length && !char.IsDigit(value[index]))
        {
            index++;
        }

        return index - start;
    }
}
