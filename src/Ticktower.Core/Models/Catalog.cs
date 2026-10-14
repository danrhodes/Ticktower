using System.Text.Json;
using Ticktower.Core.Storage;

namespace Ticktower.Core.Models;

/// <summary>A named, id-keyed item in an importable, exportable list (types, profiles, palettes, presets).</summary>
public interface ICatalogItem
{
    string Id { get; set; }
    string Name { get; set; }
    bool BuiltIn { get; set; }
}

/// <summary>Envelope for exported lists. <see cref="Kind"/> stops a palette file being imported as types.</summary>
public sealed class CatalogFile<T>
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public string Kind { get; set; } = "";
    public List<T> Items { get; set; } = new();
}

public sealed record MergeResult(int Added, int Replaced);

public static class CatalogIO
{
    public static void Export<T>(string path, string kind, IEnumerable<T> items) =>
        new JsonFileStore().Save(path, new CatalogFile<T> { Kind = kind, Items = items.ToList() });

    /// <summary>Reads an export. Never moves or modifies the file, unlike app data files.</summary>
    /// <exception cref="InvalidDataException">Unreadable, wrong kind, or newer than this build supports.</exception>
    public static List<T> Read<T>(string path, string kind)
    {
        CatalogFile<T>? file;
        try
        {
            file = JsonSerializer.Deserialize<CatalogFile<T>>(File.ReadAllText(path), JsonFileStore.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Not a valid Ticktower export file.", ex);
        }

        if (file is null) throw new InvalidDataException("Not a valid Ticktower export file.");
        if (!string.Equals(file.Kind, kind, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"This file holds \"{file.Kind}\", not \"{kind}\".");
        if (file.Version < 1 || file.Version > CatalogFile<T>.CurrentVersion)
            throw new InvalidDataException($"Unsupported export version {file.Version}.");
        return file.Items;
    }

    /// <summary>
    /// Merges imported items by id. Same id replaces a user item; a clash with a built-in item
    /// is kept as a new copy so built-ins are never overwritten. Imports are never marked built-in.
    /// </summary>
    public static MergeResult Merge<T>(List<T> existing, IEnumerable<T> incoming) where T : ICatalogItem
    {
        int added = 0, replaced = 0;
        foreach (var item in incoming)
        {
            item.BuiltIn = false;
            if (string.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString("N");

            var i = existing.FindIndex(e => e.Id == item.Id);
            if (i < 0)
            {
                existing.Add(item);
                added++;
            }
            else if (existing[i].BuiltIn)
            {
                item.Id = Guid.NewGuid().ToString("N");
                existing.Add(item);
                added++;
            }
            else
            {
                existing[i] = item;
                replaced++;
            }
        }
        return new MergeResult(added, replaced);
    }
}
