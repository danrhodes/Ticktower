using System.Net;
using System.Security.Cryptography;
using Ticktower.Core.Update;

namespace Ticktower.Tests;

public class UpdateCheckerTests
{
    private static readonly Version Now = new(0, 2, 0, 0);

    private static string Release(string tag = "v0.3.0", string asset = "Ticktower-v0.3.0-win-x64.exe",
        string url = "https://github.com/danrhodes/Ticktower/releases/download/v0.3.0/Ticktower-v0.3.0-win-x64.exe",
        bool draft = false, bool pre = false, string? digest = null) =>
        $$"""
        {"tag_name":"{{tag}}","draft":{{draft.ToString().ToLowerInvariant()}},"prerelease":{{pre.ToString().ToLowerInvariant()}},
         "html_url":"https://github.com/danrhodes/Ticktower/releases/tag/{{tag}}",
         "assets":[{"name":"notes.txt","browser_download_url":"https://x/notes.txt"},
                   {"name":"{{asset}}","browser_download_url":"{{url}}"{{(digest is null ? "" : $",\"digest\":\"{digest}\"")}}}]}
        """;

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("v0.3.0-beta.1", 0, 3, 0)]
    [InlineData("1.2.3+abc123", 1, 2, 3)]
    public void Versions_parse(string text, int major, int minor, int build)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var v));
        Assert.Equal(new Version(major, minor, build, 0), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("latest")]
    [InlineData("v1")]
    [InlineData("99999999999.1")]
    public void Bad_versions_are_rejected(string? text) => Assert.False(ReleaseVersion.TryParse(text, out _));

    [Fact]
    public void Newer_release_is_offered_with_its_exe()
    {
        var info = UpdateChecker.Parse(Release(), Now);
        Assert.NotNull(info);
        Assert.Equal(new Version(0, 3, 0, 0), info!.Version);
        Assert.Equal("Ticktower-v0.3.0-win-x64.exe", info.AssetName);
        Assert.Null(info.Sha256);
    }

    [Fact]
    public void Same_or_older_release_is_not_offered()
    {
        Assert.Null(UpdateChecker.Parse(Release(tag: "v0.2.0"), Now));
        Assert.Null(UpdateChecker.Parse(Release(tag: "v0.1.9"), Now));
    }

    [Fact]
    public void Drafts_and_prereleases_are_ignored()
    {
        Assert.Null(UpdateChecker.Parse(Release(draft: true), Now));
        Assert.Null(UpdateChecker.Parse(Release(pre: true), Now));
    }

    [Fact]
    public void Non_https_or_non_exe_assets_are_ignored()
    {
        Assert.Null(UpdateChecker.Parse(Release(url: "http://github.com/a.exe"), Now));
        Assert.Null(UpdateChecker.Parse(Release(asset: "Ticktower.zip"), Now));
        Assert.Null(UpdateChecker.Parse(Release(asset: "Other-v0.3.0.exe"), Now));
    }

    [Fact]
    public void Published_digest_is_captured()
    {
        var info = UpdateChecker.Parse(Release(digest: "sha256:ABCDEF"), Now);
        Assert.Equal("abcdef", info!.Sha256);
    }

    [Fact]
    public void Unexpected_json_shapes_do_not_throw()
    {
        Assert.Null(UpdateChecker.Parse("[]", Now));
        Assert.Null(UpdateChecker.Parse("{}", Now));
        Assert.Null(UpdateChecker.Parse("""{"tag_name":"v9.0.0","assets":5}""", Now));
    }

    private sealed class Stub(HttpStatusCode code, byte[]? body = null, string? text = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(code);
            if (body is not null) response.Content = new ByteArrayContent(body);
            else if (text is not null) response.Content = new StringContent(text);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task No_releases_yet_means_no_update()
    {
        var checker = new UpdateChecker(new HttpClient(new Stub(HttpStatusCode.NotFound)));
        Assert.Null(await checker.CheckAsync(Now));
    }

    [Fact]
    public async Task Server_errors_throw_so_the_caller_can_stay_quiet()
    {
        var checker = new UpdateChecker(new HttpClient(new Stub(HttpStatusCode.InternalServerError)));
        await Assert.ThrowsAsync<HttpRequestException>(() => checker.CheckAsync(Now));
    }

    [Fact]
    public async Task Check_reads_the_latest_release()
    {
        var checker = new UpdateChecker(new HttpClient(new Stub(HttpStatusCode.OK, text: Release())));
        Assert.NotNull(await checker.CheckAsync(Now));
    }

    private static byte[] FakeExe(int size = 4096)
    {
        var b = new byte[size];
        b[0] = (byte)'M'; b[1] = (byte)'Z';
        return b;
    }

    private static UpdateInfo Info(string? sha = null) => new(new Version(1, 0), "v1.0.0", "Ticktower.exe", "https://x/Ticktower.exe", sha, "https://x");

    [Fact]
    public async Task Download_writes_a_verified_file()
    {
        var bytes = FakeExe();
        var path = Path.Combine(Path.GetTempPath(), $"tt-{Guid.NewGuid():N}.exe");
        try
        {
            var checker = new UpdateChecker(new HttpClient(new Stub(HttpStatusCode.OK, bytes)));
            await checker.DownloadAsync(Info(Convert.ToHexString(SHA256.HashData(bytes))), path);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Checksum_mismatch_deletes_the_download()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tt-{Guid.NewGuid():N}.exe");
        var checker = new UpdateChecker(new HttpClient(new Stub(HttpStatusCode.OK, FakeExe())));
        await Assert.ThrowsAsync<InvalidDataException>(() => checker.DownloadAsync(Info(new string('0', 64)), path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Non_executable_download_is_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tt-{Guid.NewGuid():N}.exe");
        var checker = new UpdateChecker(new HttpClient(new Stub(HttpStatusCode.OK, new byte[4096])));
        await Assert.ThrowsAsync<InvalidDataException>(() => checker.DownloadAsync(Info(), path));
        Assert.False(File.Exists(path));
    }
}
