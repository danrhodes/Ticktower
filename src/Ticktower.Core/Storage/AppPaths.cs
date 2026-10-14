namespace Ticktower.Core.Storage;

/// <summary>File locations under the data folder (<c>%AppData%\Ticktower</c> by default).</summary>
public sealed class AppPaths
{
    public AppPaths(string root) => Root = root;

    public static AppPaths Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ticktower"));

    public string Root { get; }
    public string State => Path.Combine(Root, "state.json");
    public string Types => Path.Combine(Root, "types.json");
}
