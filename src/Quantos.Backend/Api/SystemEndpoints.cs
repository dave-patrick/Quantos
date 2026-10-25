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
            if (!ctx.IsNative()) return Results.Json(new { error = "Only the Windows client or CLI can stop the backend." }, statusCode: 403);
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
            if (!ctx.IsNative()) return Results.Json(new { error = "Not allowed." }, statusCode: 403);
            var kind = body?.Kind == "app" ? SessionKind.App : SessionKind.Browser;
            var code = tokens.IssueCode(kind);
            var to = body?.To is { } t && t.StartsWith('/') ? "&to=" + Uri.EscapeDataString(t) : "";
            return Results.Ok(new { code, url = $"{address.Url}auth/open?code={code}{to}" });
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
        api.MapGet("/events", async (HttpContext ctx, VaultHost host, ClientPresence presence, IHostApplicationLifetime lifetime, CancellationToken requestAborted) =>
        {
            // End the stream when the backend shuts down, so open pages don't hold up a safe exit.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, lifetime.ApplicationStopping);
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
