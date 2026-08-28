using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

internal sealed record GitConflictIndexEntry(
    string RelativePath,
    string? AncestorObject,
    string? YoursObject,
    string? TheirsObject,
    string? AncestorMode,
    string? YoursMode,
    string? TheirsMode)
{
    public bool UsesOnlyRegularFiles => new[] { AncestorMode, YoursMode, TheirsMode }
        .Where(mode => mode is not null)
        .All(mode => mode!.StartsWith("100", StringComparison.Ordinal));

    public GitConflictFileInfo ToInfo()
    {
        return new(
            RelativePath,
            AncestorObject is not null,
            YoursObject is not null,
            TheirsObject is not null);
    }
}

internal static class GitConflictIndex
{
    public static bool TryParse(string output, out IReadOnlyList<GitConflictIndexEntry>? entries)
    {
        entries = null;
        Dictionary<string, ConflictBuilder> builders = new(StringComparer.OrdinalIgnoreCase);
        foreach (string record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = record.IndexOf('\t');
            if (tab <= 0 || tab == record.Length - 1)
            {
                return false;
            }

            string[] metadata = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length != 3
                || metadata[1].Length == 0
                || !int.TryParse(metadata[2], out int stage)
                || stage is < 1 or > 3)
            {
                return false;
            }

            string path = record[(tab + 1)..].Replace('\\', '/');
            if (!builders.TryGetValue(path, out ConflictBuilder? builder))
            {
                builder = new(path);
                builders.Add(path, builder);
            }

            if (!builder.Set(stage, metadata[0], metadata[1]))
            {
                return false;
            }
        }

        entries = builders.Values
            .OrderBy(builder => builder.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(builder => builder.Build())
            .ToArray();
        return true;
    }

    private sealed class ConflictBuilder(string relativePath)
    {
        public string RelativePath { get; } = relativePath;

        private string? AncestorObject;

        private string? YoursObject;

        private string? TheirsObject;

        private string? AncestorMode;

        private string? YoursMode;

        private string? TheirsMode;

        public bool Set(int stage, string mode, string objectId)
        {
            return stage switch
            {
                1 => TrySet(ref AncestorObject, ref AncestorMode, mode, objectId),
                2 => TrySet(ref YoursObject, ref YoursMode, mode, objectId),
                3 => TrySet(ref TheirsObject, ref TheirsMode, mode, objectId),
                _ => throw new ArgumentOutOfRangeException(nameof(stage)),
            };
        }

        private static bool TrySet(
            ref string? objectTarget,
            ref string? modeTarget,
            string mode,
            string objectId)
        {
            if (objectTarget is not null || modeTarget is not null)
            {
                return false;
            }

            objectTarget = objectId;
            modeTarget = mode;
            return true;
        }

        public GitConflictIndexEntry Build()
        {
            return new(
                RelativePath,
                AncestorObject,
                YoursObject,
                TheirsObject,
                AncestorMode,
                YoursMode,
                TheirsMode);
        }
    }
}
