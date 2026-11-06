namespace Augit.Infrastructure.Updates;

/// <summary>
/// 更新检查与发布清单使用的语义化版本比较。
///
/// 只实现 Augit 需要的 SemVer 子集：三段数字、可选的 <c>-prerelease</c> 标识，
/// 构建元数据（<c>+commit</c>）不参与比较。预发布标识按 SemVer 2.0 的规定比较，
/// 数字标识低于同位置的文本标识。
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private readonly string[] _prerelease;

    private SemanticVersion(int major, int minor, int patch, string[] prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _prerelease = prerelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public bool IsPrerelease => _prerelease.Length > 0;

    public static bool TryParse(string? value, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string text = value.Trim();
        int buildIndex = text.IndexOf('+');
        if (buildIndex >= 0)
        {
            text = text[..buildIndex];
        }

        string[] parts = text.Split('-', 2);
        string[] core = parts[0].Split('.');
        if (core.Length != 3
            || !int.TryParse(core[0], out int major)
            || !int.TryParse(core[1], out int minor)
            || !int.TryParse(core[2], out int patch)
            || major < 0 || minor < 0 || patch < 0)
        {
            return false;
        }

        if (parts.Length == 2 && parts[1].Length == 0)
        {
            return false;
        }

        string[] prerelease = parts.Length == 2 && parts[1].Length > 0
            ? parts[1].Split('.')
            : [];
        if (prerelease.Any(part => part.Length == 0
            || !part.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;

        if (!IsPrerelease && !other.IsPrerelease) return 0;
        if (!IsPrerelease) return 1;
        if (!other.IsPrerelease) return -1;

        int length = Math.Min(_prerelease.Length, other._prerelease.Length);
        for (int index = 0; index < length; index++)
        {
            string left = _prerelease[index];
            string right = other._prerelease[index];
            bool leftNumeric = long.TryParse(left, out long leftNumber);
            bool rightNumeric = long.TryParse(right, out long rightNumber);
            if (leftNumeric && rightNumeric)
            {
                int numeric = leftNumber.CompareTo(rightNumber);
                if (numeric != 0) return numeric;
            }
            else if (leftNumeric != rightNumeric)
            {
                return leftNumeric ? -1 : 1;
            }
            else
            {
                int text = string.CompareOrdinal(left, right);
                if (text != 0) return text;
            }
        }

        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', _prerelease));
    public static bool operator ==(SemanticVersion? left, SemanticVersion? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

    public static bool operator <(SemanticVersion? left, SemanticVersion? right)
        => (left?.CompareTo(right) ?? (right is null ? 0 : -1)) < 0;

    public static bool operator <=(SemanticVersion? left, SemanticVersion? right) => left < right || left == right;

    public static bool operator >(SemanticVersion? left, SemanticVersion? right) => !(left <= right);

    public static bool operator >=(SemanticVersion? left, SemanticVersion? right) => !(left < right);


    public override string ToString()
    {
        string core = $"{Major}.{Minor}.{Patch}";
        return IsPrerelease ? $"{core}-{string.Join('.', _prerelease)}" : core;
    }
}
