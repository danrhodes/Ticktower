using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows.Threading;
using Ticktower.Core.Update;

namespace Ticktower;

/// <summary>
/// Checks GitHub Releases for a newer exe and swaps it in. A running exe can be renamed but not overwritten, so the
/// old file is renamed to <c>.old</c>, the new one takes its name, and the new process waits for this one to exit.
/// </summary>
public sealed class UpdateService
{
    public const string RestartArg = "--updated-from";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly AppHost _host;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly UpdateChecker _checker;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(1) };
    private bool _busy;

    public UpdateService(AppHost host)
    {
        _host = host;
        _checker = new UpdateChecker(_http);
        _timer.Tick += async (_, _) => await CheckAsync(manual: false);
    }

    /// <summary>The newest release found and not skipped, or null.</summary>
    public UpdateInfo? Available { get; private set; }

    public Version Current { get; } = ReleaseVersion.Current(Assembly.GetExecutingAssembly());

    /// <summary>Raised on the UI thread when a check finds a release the user has not skipped.</summary>
    public event Action<UpdateInfo>? Found;

    /// <summary>The exe path if this process can replace itself (a published Ticktower exe, not "dotnet run").</summary>
    public static string? SelfPath
    {
        get
        {
            var path = Environment.ProcessPath;
            return path is not null && Path.GetFileName(path).StartsWith("Ticktower", StringComparison.OrdinalIgnoreCase) ? path : null;
        }
    }

    public void Start()
    {
        CleanUpOldExe();
        _timer.Start();
        _ = CheckAsync(manual: false);
    }

    /// <summary>Returns the update (or null) and an error message. Automatic checks never report errors.</summary>
    public async Task<(UpdateInfo? Update, string? Error)> CheckAsync(bool manual)
    {
        var s = _host.Settings;
        if (!manual)
        {
            if (!s.CheckForUpdates || _busy) return (null, null);
            if (s.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < Interval) return (null, null);
        }
        if (_busy) return (null, "A check is already running.");
        _busy = true;
        try
        {
            var update = await _checker.CheckAsync(Current);
            s.LastUpdateCheckUtc = DateTime.UtcNow;
            _host.PersistSettings();
            Available = update;
            if (update is not null && (manual || s.SkippedVersion != update.Tag)) Found?.Invoke(update);
            return (update, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return (null, manual ? "Could not reach GitHub to check for updates." : null);
        }
        finally { _busy = false; }
    }

    public void Skip(UpdateInfo update)
    {
        _host.Settings.SkippedVersion = update.Tag;
        _host.PersistSettings();
    }

    /// <summary>Downloads and swaps the exe, then starts the new copy. Returns an error message, or null when the caller should exit.</summary>
    public async Task<string?> InstallAsync(UpdateInfo update)
    {
        var self = SelfPath;
        if (self is null) return "This copy cannot update itself (it is not a published Ticktower exe).";
        var staged = self + ".new";
        var old = self + ".old";
        try
        {
            await _checker.DownloadAsync(update, staged);
            if (File.Exists(old)) File.Delete(old);
            File.Move(self, old);
            try { File.Move(staged, self); }
            catch
            {
                File.Move(old, self);   // put the working copy back
                throw;
            }
            Process.Start(new ProcessStartInfo(self, $"{RestartArg} {Environment.ProcessId}") { UseShellExecute = false });
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            try { File.Delete(staged); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return $"Update failed: {ex.Message}";
        }
    }

    /// <summary>When started by an update, waits for the previous process to exit so the single-instance lock is free.</summary>
    public static void WaitForPredecessor(string[] args)
    {
        var i = Array.IndexOf(args, RestartArg);
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out var pid)) return;
        try
        {
            using var parent = Process.GetProcessById(pid);
            parent.WaitForExit(15000);
        }
        catch (ArgumentException) { }   // already gone
        catch (InvalidOperationException) { }
    }

    private static void CleanUpOldExe()
    {
        if (SelfPath is not { } self) return;
        try { File.Delete(self + ".old"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
