using System.Security.Cryptography;
using System.Text;
using Quantos.Core;
using Quantos.Core.Storage;
using Quantos.Core.Sync;
using Quantos.Core.Security;
using ModelContextProtocol.AspNetCore;

// This service owns the shared vault. Device apps keep their full local backend and sync to it.
// It exposes no local-machine API, shell, host filesystem browser, launch token or client secrets.
namespace Quantos.Server;

public static class Program
{
public static void Main(string[] args) => Build(args).Run();

public static WebApplication Build(string[] args, string? dataFolder = null, string? tokenFile = null)
{
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => { k.AddServerHeader = false; k.Limits.MaxRequestBodySize = SyncFiles.MaxFileBytes; });
var data = dataFolder ?? Environment.GetEnvironmentVariable("QUANTOS_SERVER_DATA") ?? "/data";
var secretFile = tokenFile ?? Environment.GetEnvironmentVariable("QUANTOS_SERVER_TOKEN_FILE") ?? Path.Combine(data, "server-token");
if (!File.Exists(secretFile)) throw new InvalidOperationException("Create a private server-token file containing at least 32 random characters before starting Quantos Server.");
var secret = File.ReadAllText(secretFile).Trim();
if (secret.Length < 32) throw new InvalidOperationException("The server token must contain at least 32 random characters.");
var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
var mcpFile = Environment.GetEnvironmentVariable("QUANTOS_MCP_TOKEN_FILE") ?? Path.Combine(data, "mcp-token");
var mcpToken = File.Exists(mcpFile) ? File.ReadAllText(mcpFile).Trim() : "";
var mcpHash = mcpToken.Length >= 32 ? SHA256.HashData(Encoding.UTF8.GetBytes(mcpToken)) : null;
var paths = new QuantosPaths(Path.Combine(data, "client-data"));
var vault = new VaultSession(Path.Combine(data, "Vault"), paths);
vault.Start();
var identityFile = Path.Combine(data, "server-id");
if (!File.Exists(identityFile)) AtomicFile.WriteAllText(identityFile, Guid.NewGuid().ToString());
var identity = File.ReadAllText(identityFile).Trim();
var recovery = Path.Combine(data, "sync-recovery");
builder.Services.AddSingleton(vault);
builder.Services.AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless).WithTools<SharedVaultTools>();
var app = builder.Build();
app.Lifetime.ApplicationStopped.Register(vault.Dispose);
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    if (ctx.Request.Path == "/health") { await next(); return; }
    var isMcp = ctx.Request.Path.StartsWithSegments("/mcp");
    var expectedHash = isMcp ? mcpHash : secretHash;
    var auth = ctx.Request.Headers.Authorization.ToString();
    if (expectedHash is null || !auth.StartsWith("Bearer ", StringComparison.Ordinal) ||
        !CryptographicOperations.FixedTimeEquals(expectedHash, SHA256.HashData(Encoding.UTF8.GetBytes(auth[7..]))))
    { ctx.Response.StatusCode = 401; return; }
    // Browser cross-origin access is intentionally absent. Only authenticated device backends sync.
    if (ctx.Request.Headers.Origin.Count > 0) { ctx.Response.StatusCode = 403; return; }
    try { await next(); }
    catch (QuantosException ex) { ctx.Response.StatusCode = ex.Status; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (IOException) { ctx.Response.StatusCode = 503; await ctx.Response.WriteAsJsonAsync(new { error = "The vault is busy. Retry sync." }); }
});
app.MapGet("/health", () => new { ok = true, version = "0.2.0", timeZone = vault.Clock.ZoneName });
app.MapMcp("/mcp");
app.MapGet("/sync/mcp", () => new { enabled = mcpHash is not null, endpoint = "/mcp", agent = SharedVaultTools.AgentName,
    writeAccess = vault.Permissions.IsGranted(new Actor(ActorKind.Agent, SharedVaultTools.AgentName), Permission.CreateEdit) });
app.MapPut("/sync/mcp", (McpWriteSettings settings) =>
{
    vault.Writes.Exclusive(() =>
    {
        vault.Permissions.Set(ActorKind.Agent, SharedVaultTools.AgentName, settings.WriteAccess ? [Permission.Read, Permission.CreateEdit] : [Permission.Read]);
        return true;
    });
    return new { writeAccess = settings.WriteAccess };
});
app.MapGet("/sync/manifest", () => new { serverId = identity, files = SyncFiles.Manifest(vault.Writes) });
app.MapPost("/sync/mcp/connection", () => mcpHash is null
    ? Results.Conflict(new { error = "The server MCP key is not configured." })
    : Results.Ok(new { token = mcpToken }));
app.MapGet("/sync/file", (HttpContext ctx, string path) => vault.Writes.Exclusive<IResult>(() =>
{
    var full = SyncFiles.Validate(vault.Vault, path);
    var bytes = File.Exists(full) ? File.ReadAllBytes(full) : null;
    var hash = bytes is null ? "missing" : SyncFiles.Hash(bytes);
    if (ctx.Request.Headers.IfMatch.ToString().Trim('"') != hash) return Results.Conflict(new { error = "The file changed during sync." });
    return bytes is null ? Results.NotFound() : Results.Bytes(bytes, "application/octet-stream");
}));
app.MapPut("/sync/file", async (HttpContext ctx, string path) =>
{
    var expected = ctx.Request.Headers.IfMatch.ToString().Trim('"');
    if (string.IsNullOrEmpty(expected)) return Results.BadRequest(new { error = "A base hash is required." });
    using var buffer = new MemoryStream();
    await ctx.Request.Body.CopyToAsync(buffer, ctx.RequestAborted);
    if (buffer.Length > SyncFiles.MaxFileBytes) return Results.StatusCode(413);
    var success = vault.Writes.ApplySyncFile(new(path, expected == "missing" ? null : expected, buffer.ToArray()), recovery, Actor.User);
    return success ? Results.NoContent() : Results.Conflict(new { error = "The file changed during sync." });
});
app.MapDelete("/sync/file", (HttpContext ctx, string path) =>
{
    var expected = ctx.Request.Headers.IfMatch.ToString().Trim('"');
    if (string.IsNullOrEmpty(expected)) return Results.BadRequest(new { error = "A base hash is required." });
    return vault.Writes.ApplySyncFile(new(path, expected == "missing" ? null : expected, null), recovery, Actor.User)
        ? Results.NoContent() : Results.Conflict(new { error = "The file changed during sync." });
});
return app;
}
}

public sealed record McpWriteSettings(bool WriteAccess);
