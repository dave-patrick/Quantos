using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Quantos.Core;
using Quantos.Core.Updates;

namespace Quantos.Tests;

/// <summary>Updates: check, verified download, and unpacking only the program files.</summary>
public class UpdateTests
{
    private sealed class FakeGitHub(byte[] zip, string? digest, string tag = "v0.2.0") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith("/releases/latest"))
            {
                var digestJson = digest is null ? "" : $",\"digest\":\"{digest}\"";
                var json = "{\"tag_name\":\"" + tag + "\",\"name\":\"Quantos " + tag + "\",\"body\":\"New things\",\"html_url\":\"https://example/rel\",\"published_at\":\"2026-10-19T00:00:00Z\","
                    + "\"assets\":[{\"name\":\"Quantos-0.2.0-win-x64.zip\",\"size\":" + zip.Length + ",\"browser_download_url\":\"http://fake/download.zip\"" + digestJson + "}]}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
            if (url.EndsWith("/download.zip")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static byte[] Zip(params string[] names)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in names)
            {
                using var w = new StreamWriter(archive.CreateEntry(name).Open());
                w.Write("content of " + name);
            }
        return ms.ToArray();
    }

    private static string Sha(byte[] b) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(b));

    private static (UpdateService, string) Service(byte[] zip, string? digest, string tag = "v0.2.0")
    {
        var dir = Path.Combine(Path.GetTempPath(), "quantos-upd-" + Guid.NewGuid().ToString("N"));
        return (new UpdateService("0.1.2", dir, "http://fake", "me/q", new FakeGitHub(zip, digest, tag)), dir);
    }

    [Fact]
    public async Task Finds_a_newer_release_downloads_verifies_and_unpacks_only_the_programs()
    {
        var zip = Zip("Quantos/application/Quantos.exe", "Quantos/application/Quantos.Backend.exe", "Quantos/application/quantos-cli.exe",
            "Quantos/Example Vault/note.md", "Quantos/application/sub/evil.dll", "Quantos/USER-GUIDE.md");
        var (updates, dir) = Service(zip, Sha(zip));
        using var _ = updates;
        var status = await updates.CheckAsync();
        Assert.True(status.Available);
        Assert.Equal("0.2.0", status.Latest!.Version);

        status = await updates.DownloadAsync();
        Assert.Equal("ready", status.State);
        Assert.Equal("0.2.0", status.Ready);
        var app = Path.Combine(dir, "updates", "0.2.0", "application");
        Assert.Equal(new HashSet<string> { "Quantos.Backend.exe", "Quantos.exe", "quantos-cli.exe" }, Directory.GetFiles(app).Select(f => Path.GetFileName(f)!).ToHashSet());
        Assert.False(Directory.Exists(Path.Combine(app, "sub")));
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task A_package_that_isnt_laid_out_for_the_updater_is_refused_with_a_clear_reason()
    {
        // A package zipped by hand (no Quantos\ wrapper, loose DLLs) cannot be installed: the updater
        // only takes Quantos\application\<file>, and copies single files from that folder.
        var zip = Zip("application/Quantos.exe", "application/Quantos.Backend.exe", "application/quantos-cli.exe",
            "application/Quantos.Backend.dll", "application/wwwroot/index.html");
        var (updates, dir) = Service(zip, Sha(zip));
        using var _ = updates;
        await updates.CheckAsync();
        var ex = await Assert.ThrowsAsync<QuantosException>(() => updates.DownloadAsync());
        Assert.Contains("Quantos.exe", ex.Message);
        Assert.Contains("isn't a Quantos package", ex.Message);
        Assert.Null(updates.Status().Ready);
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task A_download_that_doesnt_match_its_checksum_is_thrown_away()
    {
        var zip = Zip("Quantos/application/Quantos.exe", "Quantos/application/Quantos.Backend.exe", "Quantos/application/quantos-cli.exe");
        var (updates, dir) = Service(zip, "sha256:" + new string('0', 64));
        using var _ = updates;
        await updates.CheckAsync();
        var ex = await Assert.ThrowsAsync<QuantosException>(() => updates.DownloadAsync());
        Assert.Contains("checksum", ex.Message);
        Assert.Null(updates.Status().Ready);
        Assert.Empty(Directory.GetFiles(Path.Combine(dir, "updates")));
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task No_checksum_means_no_download_and_older_releases_arent_offered()
    {
        var zip = Zip("Quantos/application/Quantos.exe");
        var (noDigest, d1) = Service(zip, null);
        using (noDigest)
        {
            await noDigest.CheckAsync();
            await Assert.ThrowsAsync<QuantosException>(() => noDigest.DownloadAsync());
        }
        var (older, d2) = Service(zip, Sha(zip), "v0.1.0");
        using (older) Assert.False((await older.CheckAsync()).Available);
        foreach (var d in new[] { d1, d2 }) if (Directory.Exists(d)) Directory.Delete(d, true);
    }
}

/// <summary>Opt-in (QUANTOS_LIVE_UPDATE=1): the real GitHub release, downloaded, verified and unpacked as if from 0.1.1.</summary>
public class LiveUpdateTests
{
    [Fact]
    public async Task Real_release_downloads_and_verifies()
    {
        if (Environment.GetEnvironmentVariable("QUANTOS_LIVE_UPDATE") != "1") return;
        var dir = Path.Combine(Path.GetTempPath(), "quantos-live-" + Guid.NewGuid().ToString("N"));
        using var updates = new UpdateService("0.1.1", dir, "https://api.github.com");
        var status = await updates.CheckAsync();
        Assert.True(status.Available);
        status = await updates.DownloadAsync();
        Assert.Equal("ready", status.State);
        Assert.True(File.Exists(Path.Combine(dir, "updates", status.Ready!, "application", "Quantos.exe")));
        Directory.Delete(dir, true);
    }
}
