using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Quantos.Core.Storage;

namespace Quantos.Core.Sync;

public sealed class HttpSyncRemote : ISyncRemote, IDisposable
{
    private readonly HttpClient http;
    private readonly string identityFile;
    public HttpSyncRemote(string url, string token, string identityFile)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new QuantosException("Use the server’s HTTPS address, without a password or query string.");
        this.identityFile = identityFile;
        http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(10) }) { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
    private sealed record ManifestDto(string ServerId, List<SyncFile> Files);
    public async Task<IReadOnlyList<SyncFile>> Manifest(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); ct = deadline.Token;
        var manifest = await http.GetFromJsonAsync<ManifestDto>("sync/manifest", ct) ?? throw new QuantosException("The server returned an empty manifest.");
        if (!Guid.TryParse(manifest.ServerId, out _)) throw new QuantosException("The server identity is invalid.");
        if (manifest.Files.Count > 100000 || manifest.Files.Any(f => f.Size < 0 || f.Size > SyncFiles.MaxFileBytes || f.Hash.Length != 64 || f.Hash.Any(c => !Uri.IsHexDigit(c))))
            throw new QuantosException("The server manifest is invalid.");
        if (File.Exists(identityFile))
        {
            if (File.ReadAllText(identityFile) != manifest.ServerId) throw new QuantosException("This is a different server vault. Disconnect and connect it as a new shared vault.");
        }
        else AtomicFile.WriteAllText(identityFile, manifest.ServerId);
        return manifest.Files;
    }
    public async Task<byte[]?> Read(string path, string? expectedHash, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "sync/file?path=" + Uri.EscapeDataString(path));
        request.Headers.TryAddWithoutValidation("If-Match", '"' + (expectedHash ?? "missing") + '"');
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound && expectedHash is null) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > SyncFiles.MaxFileBytes) throw new QuantosException("The server file exceeds 200 MB.");
        using var input = await response.Content.ReadAsStreamAsync(ct);
        if (response.Content.Headers.ContentLength is { } length)
        {
            var content = GC.AllocateUninitializedArray<byte>(checked((int)length));
            await input.ReadExactlyAsync(content, ct);
            return content;
        }
        using var buffer = new MemoryStream();
        var bytes = new byte[81920]; int count;
        while ((count = await input.ReadAsync(bytes, ct)) > 0)
        {
            if (buffer.Length + count > SyncFiles.MaxFileBytes) throw new QuantosException("The server file exceeds 200 MB.");
            buffer.Write(bytes, 0, count);
        }
        return buffer.ToArray();
    }
    public async Task<bool> Apply(SyncMutation mutation, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(mutation.Content is null ? HttpMethod.Delete : HttpMethod.Put, "sync/file?path=" + Uri.EscapeDataString(mutation.Path));
        request.Headers.TryAddWithoutValidation("If-Match", '"' + (mutation.ExpectedHash ?? "missing") + '"');
        if (mutation.Content is not null) request.Content = new ByteArrayContent(mutation.Content);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Conflict) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }
    public async Task<JsonElement> McpSettings(bool? writeAccess, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); ct = deadline.Token;
        if (writeAccess is { } allowed)
        {
            using var response = await http.PutAsJsonAsync("sync/mcp", new { writeAccess = allowed }, ct);
            response.EnsureSuccessStatusCode();
        }
        return await http.GetFromJsonAsync<JsonElement>("sync/mcp", ct);
    }
    public async Task<string> McpConnectionKey(CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("sync/mcp/connection", new { }, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("token").GetString()!;
    }
    public void Dispose() => http.Dispose();
}
