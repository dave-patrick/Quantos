using System.Text.Json;
using System.Threading.Channels;
using System.Reflection;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Security;

namespace Quantos.Backend.Api;

public static class SystemEndpoints
{
    /// <summary>
    /// The version this build reports. A patch release (0.1.5.2) is reported whole: with only the
    /// first three parts the app would think it was still 0.1.5 and offer the update again.
    /// </summary>
    public static readonly string Version = VersionOf(typeof(SystemEndpoints).Assembly);

    private static string VersionOf(Assembly assembly)
    {
        var v = assembly.GetName().Version;
        if (v is null) return "0.0.0";
        return v.Revision > 0 ? v.ToString(4) : v.ToString(3);
    }

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/ping", () => new { ok = true, version = Version });

        api.MapGet("/status", (VaultHost host, ServerAddress address, ClientPresence presence, HttpContext ctx) =>
        {
            var session = host.Session;
            return new
            {
                version = Version,
                pid = Environment.ProcessId,
                port = address.Port,
                clientData = host.Paths.ClientData,
                vault = session?.Vault.Root,
                workspaces = session?.Vault.WorkspaceNames(),
                linked = session?.Vault.LinkedRoots.Select(kv => new { name = kv.Key, path = kv.Value, exists = Directory.Exists(kv.Value) }).ToList(),
                lastWorkspace = host.Settings.LastWorkspace,
                nodeCount = session?.Cache.Count(),
                syncedFolderWarning = session?.SyncedFolderWarning,
                reconcile = session?.LastReconcile,
                safeMode = session?.Permissions.SafeMode ?? false,
                windowsClientConnected = presence.WindowsClientConnected,
                timeZone = new Clock().ZoneName,
                utcOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).ToString(),
                actor = new { kind = ctx.Actor().Kind, name = ctx.Actor().Name, native = ctx.IsNative(), windowsClient = ctx.IsWindowsClient() }
            };
        });

        api.MapPost("/system/rebuild", (VaultHost host, HttpContext ctx) =>
        {
            var session = host.Require();
            session.Permissions.Demand(ctx.Actor(), Permission.CreateEdit, "Rebuild the cache");
            return session.RebuildCache();
        });

        api.MapPost("/system/shutdown", (HttpContext ctx, IHostApplicationLifetime lifetime) =>
        {
            if (!ctx.IsAppClient() && ctx.Actor().Kind != ActorKind.Cli) return Results.Json(new { error = "Only a native client or paired CLI can stop the backend." }, statusCode: 403);
            _ = Task.Run(async () => { await Task.Delay(200); lifetime.StopApplication(); });
            return Results.Ok(new { stopping = true });
        });

        api.MapPost("/system/workspace", (VaultHost host, WorkspaceBody body) =>
        {
            host.Settings.LastWorkspace = body.Name;
            host.Settings.Save(host.Paths);
            return Results.NoContent();
        });

        // Vault selection (first run and switching)
        api.MapGet("/setup", (VaultHost host) => new
        {
            vault = host.Session?.Vault.Root,
            defaultVault = QuantosPaths.DefaultVault,
            exampleVault = QuantosPaths.ExampleVault,
            recent = host.Settings.RecentVaults,
            defaultCheck = VaultHost.Check(QuantosPaths.DefaultVault)
        });

        api.MapPost("/setup/check", (VaultPathBody body) => VaultHost.Check(body.Path));

        api.MapPost("/setup/open", (VaultHost host, VaultPathBody body) => host.Open(body.Path));

        // Browser handoff: native callers (Windows client, CLI) get a one-time code for a browser session.
        api.MapPost("/auth/code", (HttpContext ctx, AccessTokens tokens, ServerAddress address, CodeBody? body) =>
        {
            // A paired personal CLI may open ordinary browser windows, never app/native sessions.
            if (!ctx.IsAppClient() && !(ctx.Actor().Kind == ActorKind.Cli && body?.Kind != "app"))
                return Results.Json(new { error = "Only a native client or paired personal CLI can sign in a browser." }, statusCode: 403);
            var kind = body?.Kind == "app" ? SessionKind.App : SessionKind.Browser;
            var code = tokens.IssueCode(kind);
            var to = body?.To is { } t && t.StartsWith('/') ? "&to=" + Uri.EscapeDataString(t) : "";
            return Results.Ok(new { code, url = $"{address.Url}auth/open?code={code}{to}" });
        });

        api.MapGet("/auth/whoami", (HttpContext ctx) => new { kind = ctx.Actor().Kind, name = ctx.Actor().Name, credentialId = ctx.Actor().CredentialId, native = ctx.IsAppClient() });

        api.MapPost("/auth/credentials/request", (VaultHost host, ClientCredentials credentials, HttpContext ctx, PairingBody body) =>
        {
            if (!ctx.IsBootstrap()) throw new QuantosException("Use the discovery credential only to request pairing.", 403);
            var approval = credentials.Request(body.Kind, body.Name, body.Proof, host.Require().Approvals);
            return Results.Accepted(value: new { id = approval.Id });
        });

        api.MapPost("/auth/credentials/poll", (VaultHost host, ClientCredentials credentials, HttpContext ctx, PairingPollBody body) =>
        {
            if (!ctx.IsBootstrap()) throw new QuantosException("Use the discovery credential only to finish pairing.", 403);
            return credentials.Poll(body.Id, body.Proof, host.Require().Approvals);
        });

        api.MapGet("/auth/credentials", (ClientCredentials credentials, HttpContext ctx) =>
        {
            if (ctx.Actor().Kind != ActorKind.User) throw new QuantosException("Only you can manage paired connections.", 403);
            return credentials.List();
        });

        api.MapPost("/auth/credentials/{id}/revoke", (VaultHost host, ClientCredentials credentials, HttpContext ctx, string id, CredentialRevokeBody body) =>
        {
            if (ctx.Actor().Kind != ActorKind.User) throw new QuantosException("Only you can manage paired connections.", 403);
            var info = credentials.List().FirstOrDefault(c => c.Id == id) ?? throw new QuantosException("That connection is no longer paired.", 404);
            host.Require().Permissions.Demand(ctx.Actor(), Permission.ManagePermissions, $"Revoke {info.Kind} connection “{info.Name}” ({info.Id})", body.Approval);
            return credentials.Revoke(id) ? Results.NoContent() : Results.NotFound();
        });

        // Approvals: requested by anyone, decided only by the Windows client after a native prompt.
        api.MapGet("/approvals", (VaultHost host) => host.Require().Approvals.Pending());

        api.MapGet("/approvals/{id}", (VaultHost host, string id) =>
            host.Require().Approvals.Get(id) is { } a ? Results.Ok(a) : Results.NotFound());

        api.MapPost("/approvals/{id}", (VaultHost host, HttpContext ctx, string id, DecisionBody body) =>
        {
            if (!ctx.IsAppClient())
                return Results.Json(new { error = "Approvals can only be given in a native Quantos client." }, statusCode: 403);
            return host.Require().Approvals.Decide(id, body.Approve) ? Results.Ok(host.Require().Approvals.Get(id)) : Results.NotFound();
        });

        api.MapGet("/permissions", (VaultHost host) => new { grants = host.Require().Permissions.Current, safeMode = host.Require().Permissions.SafeMode });

        // Push channel (write step 7): server-sent events.
        api.MapGet("/events", async (HttpContext ctx, VaultHost host, ClientPresence presence, ClientCredentials credentials, IHostApplicationLifetime lifetime, CancellationToken requestAborted) =>
        {
            // End the stream when the backend shuts down, so open pages don't hold up a safe exit.
            var revoked = ctx.Actor() is { Kind: ActorKind.Agent or ActorKind.Cli, CredentialId: { } id }
                ? credentials.RevocationToken(id) : CancellationToken.None;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, lifetime.ApplicationStopping, revoked);
            var ct = linked.Token;
            using var present = ctx.IsWindowsClient() ? presence.WindowsClient() : null;
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
            var channel = Channel.CreateBounded<ChangeEvent>(new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.DropOldest });
            void Handler(ChangeEvent e) => channel.Writer.TryWrite(e);
            host.Changed += Handler;
            var json = ctx.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
            try
            {
                await ctx.Response.WriteAsync($"event: hello\ndata: {JsonSerializer.Serialize(new { version = Version }, json)}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
                while (!ct.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    try
                    {
                        var e = await channel.Reader.ReadAsync(timeout.Token);
                        await ctx.Response.WriteAsync($"event: change\ndata: {JsonSerializer.Serialize(e, json)}\n\n", ct);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        await ctx.Response.WriteAsync(": ping\n\n", ct);
                    }
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
            finally { host.Changed -= Handler; }
        });
    }
}

public sealed record PairingBody(ActorKind Kind, string Name, string Proof);
public sealed record PairingPollBody(string Id, string Proof);
public sealed record CredentialRevokeBody(string? Approval);
