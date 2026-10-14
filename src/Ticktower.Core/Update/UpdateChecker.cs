using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ticktower.Core.Update;

/// <summary>A newer release found on GitHub Releases.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string AssetName, string AssetUrl, string? Sha256, string PageUrl);

public static class ReleaseVersion
{
    private static readonly Regex Pattern = new(@"^v?(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:[-+].*)?$", RegexOptions.Compiled);

    /// <summary>Parses "v1.2.3", "1.2", "1.2.3-beta" or "1.2.3+sha". A suffix is ignored.</summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = Pattern.Match(text.Trim());
        if (!m.Success) return false;
        int Part(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value) : 0;
        try
        {
            version = new Version(Part(1), Part(2), Part(3), Part(4));
            return true;
        }
        catch (Exception ex) when (ex is OverflowException or FormatException) { return false; }
    }

    /// <summary>Version of the running build, from the informational version when it has one.</summary>
    public static Version Current(System.Reflection.Assembly assembly)
    {
        var info = (Attribute.GetCustomAttribute(assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute))
            as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion;
        if (TryParse(info, out var v)) return v;
        return assembly.GetName().Version ?? new Version(0, 0, 0, 0);
    }
}

/// <summary>
/// Looks for a newer GitHub release and downloads it. This is the only network use in Ticktower.
/// It sends nothing but the request itself: no identifiers, no usage data.
/// </summary>
public sealed class UpdateChecker
{
    public const string DefaultRepo = "danrhodes/Ticktower";
    private const long MaxDownloadBytes = 200L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly string _repo;

    public UpdateChecker(HttpClient http, string repo = DefaultRepo)
    {
        _http = http;
        _repo = repo;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Ticktower", "update-check"));
    }

    /// <summary>The latest published release if it is newer than <paramref name="current"/>, else null.</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_repo}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;   // no releases yet
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return Parse(json, current, _repo);
    }

    /// <summary>Pure parsing step, split out for tests. Drafts and pre-releases are ignored.</summary>
    public static UpdateInfo? Parse(string json, Version current, string repo = DefaultRepo)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (Flag(root, "draft") || Flag(root, "prerelease")) return null;

        var tag = Str(root, "tag_name");
        if (!ReleaseVersion.TryParse(tag, out var version) || version <= current) return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = Str(asset, "name");
            var url = Str(asset, "browser_download_url");
            if (name is null || url is null) continue;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.StartsWith("Ticktower", StringComparison.OrdinalIgnoreCase)) continue;
            // Only ever download over https.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;

            var digest = Str(asset, "digest");
            string? sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest[7..].ToLowerInvariant() : null;
            return new UpdateInfo(version, tag!, name, url, sha, Str(root, "html_url") ?? $"https://github.com/{repo}/releases");
        }
        return null;
    }

    /// <summary>
    /// Downloads the release exe to <paramref name="destination"/>. Verifies it looks like a Windows executable and,
    /// when GitHub published a SHA-256 digest, that the hash matches. Deletes the file and throws on any failure.
    /// </summary>
    public async Task DownloadAsync(UpdateInfo update, string destination, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            if (total > MaxDownloadBytes) throw new InvalidDataException("The update is unexpectedly large.");

            using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    done += read;
                    if (done > MaxDownloadBytes) throw new InvalidDataException("The update is unexpectedly large.");
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    if (total is > 0) progress?.Report((double)done / total.Value);
                }
            }
            Verify(destination, update.Sha256);
        }
        catch
        {
            try { File.Delete(destination); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public static void Verify(string path, string? expectedSha256)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length < 1024 || stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidDataException("The downloaded file is not a Windows program.");
        if (expectedSha256 is null) return;
        stream.Position = 0;
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (actual != expectedSha256.ToLowerInvariant())
            throw new InvalidDataException("The downloaded file does not match its published checksum.");
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
