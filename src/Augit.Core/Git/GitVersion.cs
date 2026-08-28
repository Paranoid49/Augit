using System.Globalization;

namespace Augit.Core.Git;

public readonly record struct GitVersion(int Major, int Minor, int Patch) : IComparable<GitVersion>
{
    public static GitVersion MinimumSupported { get; } = new(2, 40, 0);

    public bool IsSupported => CompareTo(MinimumSupported) >= 0;

    public static bool operator <(GitVersion left, GitVersion right) => left.CompareTo(right) < 0;

    public static bool operator <=(GitVersion left, GitVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >(GitVersion left, GitVersion right) => left.CompareTo(right) > 0;

    public static bool operator >=(GitVersion left, GitVersion right) => left.CompareTo(right) >= 0;

    public int CompareTo(GitVersion other)
    {
        int majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        int minorComparison = Minor.CompareTo(other.Minor);
        return minorComparison != 0 ? minorComparison : Patch.CompareTo(other.Patch);
    }

    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
    }

    public static bool TryParse(string? versionOutput, out GitVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(versionOutput))
        {
            return false;
        }

        ReadOnlySpan<char> text = versionOutput.AsSpan().Trim();
        const string Prefix = "git version ";
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        text = text[Prefix.Length..];
        Span<int> components = stackalloc int[3];
        for (int index = 0; index < components.Length; index++)
        {
            int digitCount = 0;
            int value = 0;
            while (digitCount < text.Length && char.IsAsciiDigit(text[digitCount]))
            {
                try
                {
                    value = checked((value * 10) + (text[digitCount] - '0'));
                }
                catch (OverflowException)
                {
                    return false;
                }

                digitCount++;
            }

            if (digitCount == 0)
            {
                return false;
            }

            components[index] = value;
            text = text[digitCount..];
            if (index < components.Length - 1)
            {
                if (text.IsEmpty || text[0] != '.')
                {
                    return false;
                }

                text = text[1..];
            }
        }

        version = new(components[0], components[1], components[2]);
        return true;
    }
}
