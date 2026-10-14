namespace Ticktower.Core.Storage;

/// <summary>File locations under the data folder (<c>%AppData%\Ticktower</c> by default).</summary>
public sealed class AppPaths
{
    public AppPaths(string root) => Root = root;

    /// <summary><c>%AppData%\Ticktower</c>, or the folder in <c>TICKTOWER_DATA</c> when set (for sandboxed runs).</summary>
    public static AppPaths Default()
    {
        var overrideDir = Environment.GetEnvironmentVariable("TICKTOWER_DATA");
        return new(!string.IsNullOrWhiteSpace(overrideDir)
            ? overrideDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ticktower"));
    }

    public string Root { get; }
    public string State => Path.Combine(Root, "state.json");
    public string Types => Path.Combine(Root, "types.json");
}
