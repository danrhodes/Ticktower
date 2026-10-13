using System.Text.Json.Nodes;

namespace Ticktower.Core.Storage;

/// <summary>Upgrades a document from <see cref="FromVersion"/> to <c>FromVersion + 1</c>.</summary>
public interface IMigration
{
    int FromVersion { get; }
    void Apply(JsonObject root);
}

/// <summary>
/// Walks a JSON document up to the current schema version using a contiguous chain of
/// single-step migrations. The <c>version</c> property is bumped after each step.
/// </summary>
public sealed class Migrator
{
    public const string VersionProperty = "version";

    private readonly SortedDictionary<int, IMigration> _steps = new();

    public int CurrentVersion { get; }

    public Migrator(int currentVersion, IEnumerable<IMigration> migrations)
    {
        if (currentVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(currentVersion));
        CurrentVersion = currentVersion;

        foreach (var m in migrations)
        {
            if (m.FromVersion < 1 || m.FromVersion >= currentVersion)
                throw new ArgumentException($"Migration from v{m.FromVersion} does not lead to a version up to v{currentVersion}.", nameof(migrations));
            if (!_steps.TryAdd(m.FromVersion, m))
                throw new ArgumentException($"Duplicate migration from v{m.FromVersion}.", nameof(migrations));
        }

        for (var v = 1; v < currentVersion; v++)
        {
            if (!_steps.ContainsKey(v))
                throw new ArgumentException($"Missing migration from v{v} to v{v + 1}.", nameof(migrations));
        }
    }

    /// <summary>Returns true when the document was changed.</summary>
    /// <exception cref="InvalidDataException">Version missing, invalid, or newer than supported.</exception>
    public bool Migrate(JsonObject root)
    {
        if (root[VersionProperty] is not JsonValue node || !node.TryGetValue<int>(out var version) || version < 1)
            throw new InvalidDataException("Document has no valid version.");
        if (version > CurrentVersion)
            throw new InvalidDataException($"Document is version {version}; this build supports up to {CurrentVersion}.");

        var changed = false;
        for (; version < CurrentVersion; version++)
        {
            _steps[version].Apply(root);
            root[VersionProperty] = version + 1;
            changed = true;
        }
        return changed;
    }
}
