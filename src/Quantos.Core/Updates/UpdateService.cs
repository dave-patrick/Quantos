using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Quantos.Core.Updates;

public sealed record UpdateSettings { public bool AutoCheck { get; init; } }

public sealed record UpdateRelease(string Version, string Name, string Notes, string Page, string Published, long Size);

public sealed record UpdateStatus(string Current, UpdateRelease? Latest, bool Available, string? LastCheck, string State,
    double Progress, string? Ready, string? Error, UpdateSettings Settings);

/// <summary>
/// Updates (after 0.1.2). Asks GitHub for the latest release only when the person checks, or daily if they turn
/// that on; nothing else ever leaves the PC. A download is checked against the SHA-256 digest GitHub publishes for
/// the release file, and only the program files (application\) are unpacked into client-data\updates\{version}.
/// The Windows client swaps them in and restarts; the vault and client-data are never touched.
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string Repository = "dave-patrick/Quantos";
    private static readonly string[] Programs = ["Quantos.exe", "Quantos.Backend.exe", "quantos-cli.exe"];
    private readonly string _api, _repo, _folder, _settingsFile;
    private readonly bool _trustedHostsOnly;
    private readonly HttpClient _http;
    private readonly object _gate = new();
    private readonly Timer? _timer;
    private UpdateRelease? _latest;
    private string? _assetUrl, _digest;
    private DateTimeOffset? _lastCheck;
    private string _state = "idle";
    private double _progress;
    private string? _error;

    public UpdateService(string current, string clientData, string? apiBase = null, string? repo = null, HttpMessageHandler? handler = null)
    {
        Current = current;
        _api = (apiBase ?? Environment.GetEnvironmentVariable("QUANTOS_UPDATE_API") ?? "https://api.github.com").TrimEnd('/');
        _trustedHostsOnly = _api == "https://api.github.com";
        _repo = repo ?? Environment.GetEnvironmentVariable("QUANTOS_UPDATE_REPO") ?? Repository;
        _folder = Path.Combine(clientData, "updates");
        _settingsFile = Path.Combine(clientData, "settings", "updates.json");
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Quantos", current));
        if (apiBase is null) _timer = new Timer(_ => { if (Settings.AutoCheck) _ = SafeCheck(); }, null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(24));
    }

    public string Current { get; }
    public string Folder => _folder;

    public UpdateSettings Settings
    {
        get
        {
            try { return File.Exists(_settingsFile) ? JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(_settingsFile), JsonSerializerOptions.Web) ?? new() : new(); }
            catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
        }
    }

    public void SaveSettings(UpdateSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
        File.WriteAllText(_settingsFile, JsonSerializer.Serialize(settings, JsonSerializerOptions.Web));
    }

    public UpdateStatus Status()
    {
        lock (_gate)
            return new UpdateStatus(Current, _latest, _latest is not null && Newer(_latest.Version, Current), _lastCheck?.ToString("o"),
                _state, _progress, ReadyVersion(), _error, Settings);
    }

    private async Task SafeCheck() { try { await CheckAsync(); } catch { /* a background check stays quiet; the status says why */ } }

    public async Task<UpdateStatus> CheckAsync(CancellationToken ct = default)
    {
        lock (_gate) { _state = "checking"; _error = null; }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_api}/repos/{_repo}/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new QuantosException($"GitHub answered {(int)response.StatusCode} when asked for the latest release.", 502);
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
            var version = (json["tag_name"]?.ToString() ?? "").TrimStart('v', 'V');
            if (!System.Version.TryParse(version, out _)) throw new QuantosException("The latest release has no version number Quantos understands.", 502);
            var asset = json["assets"]?.AsArray().FirstOrDefault(a => a?["name"]?.ToString() is { } n && n.StartsWith("Quantos-", StringComparison.Ordinal) && n.EndsWith("-win-x64.zip", StringComparison.Ordinal));
            lock (_gate)
            {
                _latest = new UpdateRelease(version, json["name"]?.ToString() ?? "Quantos " + version, json["body"]?.ToString() ?? "",
                    json["html_url"]?.ToString() ?? "", json["published_at"]?.ToString() ?? "", asset?["size"]?.GetValue<long>() ?? 0);
                _assetUrl = asset?["browser_download_url"]?.ToString();
                _digest = asset?["digest"]?.ToString();
                _lastCheck = DateTimeOffset.Now;
                _state = "idle";
            }
            return Status();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            lock (_gate) { _state = "idle"; _error = "Couldn't reach GitHub to check for updates: " + ex.Message; }
            throw new QuantosException(_error, 502);
        }
        catch (QuantosException ex) { lock (_gate) { _state = "idle"; _error = ex.Message; } throw; }
    }

    /// <summary>Downloads, verifies and unpacks the latest release's program files. Progress shows in Status().</summary>
    public async Task<UpdateStatus> DownloadAsync(CancellationToken ct = default)
    {
        string url, version; string? digest;
        lock (_gate)
        {
            if (_state == "downloading") throw new QuantosException("A download is already running.", 409);
            if (_latest is null || !Newer(_latest.Version, Current)) throw new QuantosException("There's no newer version to download. Check for updates first.");
            if (_assetUrl is null) throw new QuantosException("The latest release has no Windows download.");
            if (_digest is null || !_digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) throw new QuantosException("The release doesn't publish a checksum, so the download can't be verified. Download it from the release page instead.");
            (url, version, digest) = (_assetUrl, _latest.Version, _digest[7..].ToLowerInvariant());
            _state = "downloading"; _progress = 0; _error = null;
        }
        var zip = Path.Combine(_folder, $"Quantos-{version}.zip");
        try
        {
            if (_trustedHostsOnly && !TrustedDownload(url)) throw new QuantosException("The download isn't on GitHub, so it wasn't fetched.");
            Directory.CreateDirectory(_folder);
            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                if (_trustedHostsOnly && response.RequestMessage?.RequestUri is { } final && !TrustedDownload(final.ToString()))
                    throw new QuantosException("The download was redirected away from GitHub, so it was stopped.");
                var total = response.Content.Headers.ContentLength ?? _latest!.Size;
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var file = File.Create(zip + ".partial");
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long read = 0;
                for (int n; (n = await source.ReadAsync(buffer, ct)) > 0;)
                {
                    await file.WriteAsync(buffer.AsMemory(0, n), ct);
                    sha.AppendData(buffer, 0, n);
                    read += n;
                    if (total > 0) lock (_gate) _progress = Math.Min(0.99, (double)read / total);
                }
                if (Convert.ToHexStringLower(sha.GetHashAndReset()) != digest)
                    throw new QuantosException("The download doesn't match the checksum GitHub published for it, so it wasn't used. Try again.");
            }
            File.Move(zip + ".partial", zip, overwrite: true);
            Unpack(zip, version);
            File.Delete(zip);
            lock (_gate) { _state = "ready"; _progress = 1; }
            return Status();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or QuantosException or TaskCanceledException)
        {
            TryDelete(zip + ".partial"); TryDelete(zip);
            lock (_gate) { _state = "idle"; _progress = 0; _error = ex is QuantosException ? ex.Message : "The download failed: " + ex.Message; }
            throw ex as QuantosException ?? new QuantosException(_error!, 502);
        }
    }

    /// <summary>Only Quantos/application/* comes out of the zip, as plain file names (no folders, nothing outside).</summary>
    private void Unpack(string zip, string version)
    {
        var target = Path.Combine(_folder, version);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        var app = Path.Combine(target, "application");
        Directory.CreateDirectory(app);
        using var archive = ZipFile.OpenRead(zip);
        foreach (var entry in archive.Entries)
        {
            var parts = entry.FullName.Replace('\\', '/').Split('/');
            if (parts.Length != 3 || parts[0] != "Quantos" || parts[1] != "application" || parts[2].Length == 0) continue;
            if (parts[2] != Path.GetFileName(parts[2]) || parts[2].Contains("..")) continue;
            entry.ExtractToFile(Path.Combine(app, parts[2]), overwrite: true);
        }
        foreach (var program in Programs)
            if (!File.Exists(Path.Combine(app, program))) throw new InvalidDataException($"The download is missing {program}.");
        File.WriteAllText(Path.Combine(target, "ready.json"), JsonSerializer.Serialize(new { version, at = DateTimeOffset.Now }));
    }

    /// <summary>The newest downloaded, verified version waiting to be installed.</summary>
    public string? ReadyVersion()
    {
        if (!Directory.Exists(_folder)) return null;
        return Directory.EnumerateDirectories(_folder)
            .Where(d => File.Exists(Path.Combine(d, "ready.json")) && System.Version.TryParse(Path.GetFileName(d), out _) && Newer(Path.GetFileName(d), Current))
            .Select(Path.GetFileName).OrderByDescending(v => System.Version.Parse(v!)).FirstOrDefault();
    }

    public static bool Newer(string candidate, string current) =>
        System.Version.TryParse(candidate, out var a) && System.Version.TryParse(current, out var b) && a > b;

    private static bool TrustedDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" &&
        (u.Host == "github.com" || u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }

    public void Dispose() { _timer?.Dispose(); _http.Dispose(); }
}
