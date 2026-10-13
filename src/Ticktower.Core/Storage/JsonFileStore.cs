using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ticktower.Core.Storage;

public enum LoadOutcome
{
    /// <summary>Parsed as-is.</summary>
    Loaded,
    /// <summary>No file; defaults returned.</summary>
    Missing,
    /// <summary>Parsed after one or more migrations. Next save persists the new version.</summary>
    Migrated,
    /// <summary>Unreadable or unsupported; original moved to <c>.bad</c>, defaults returned.</summary>
    Recovered,
}

public sealed record LoadResult<T>(T Value, LoadOutcome Outcome, string? BackupPath = null);

/// <summary>Reads and writes JSON documents safely: temp file plus atomic replace, never in place.</summary>
public sealed class JsonFileStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public LoadResult<T> Load<T>(string path, Func<T> createDefault, Migrator? migrator = null)
    {
        if (!File.Exists(path))
            return new LoadResult<T>(createDefault(), LoadOutcome.Missing);

        try
        {
            var text = File.ReadAllText(path);
            var root = JsonNode.Parse(text) as JsonObject
                ?? throw new InvalidDataException("Root is not a JSON object.");

            var migrated = migrator?.Migrate(root) ?? false;
            var value = root.Deserialize<T>(Options)
                ?? throw new InvalidDataException("Document deserialised to null.");
            return new LoadResult<T>(value, migrated ? LoadOutcome.Migrated : LoadOutcome.Loaded);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            var bad = path + ".bad";
            File.Move(path, bad, overwrite: true);
            return new LoadResult<T>(createDefault(), LoadOutcome.Recovered, bad);
        }
    }

    public void Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(fs, value, Options);
            fs.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
    }
}
