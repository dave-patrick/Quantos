using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Extensibility;
using Quantos.Core.Security;

namespace Quantos.Backend.Api;

public sealed record ShellRunBody(string Command, string? Cwd, string? Approval);
public sealed record ShellResultBody(string Output, bool Failed);
public sealed record GrantBody(ActorKind Kind, string Name, List<Permission> Permissions, string? Approval);
public sealed record SafeModeBody(bool On);
public sealed record PluginToggleBody(string Name, bool Enabled);

public sealed record ShellJob(string Id, string Command, string Cwd, string RequestedBy, DateTimeOffset Requested)
{
    public string Status { get; set; } = "queued";
    public string? Output { get; set; }
    public bool Failed { get; set; }
}

public static class ExtensibilityEndpoints
{
    private static readonly ConcurrentDictionary<string, ShellJob> Jobs = new();

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Shell execution: always approved in the Windows client, then run there with the built-in PowerShell engine.
        api.MapPost("/shell/run", (VaultHost host, HttpContext ctx, ShellRunBody b) =>
        {
            var session = host.Require();
            var cwd = string.IsNullOrWhiteSpace(b.Cwd) ? session.Vault.Root : Path.GetFullPath(b.Cwd);
            if (!session.Vault.IsInside(cwd) && !string.Equals(cwd.TrimEnd('\\'), session.Vault.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                session.Permissions.Demand(ctx.Actor(), Permission.OutsideFiles, $"Use the folder {cwd}");
            session.Permissions.Demand(ctx.Actor(), Permission.RunCommands, $"Run PowerShell in {cwd}:\n{b.Command}", b.Approval);
            var job = new ShellJob(Ulid.New(), b.Command, cwd, ctx.Actor().ToString(), DateTimeOffset.Now);
            Jobs[job.Id] = job;
            session.Changes.Publish(new ChangeEvent(ChangeKind.ShellJob, null, null, Origin: ctx.Actor().ToString(), Data: job));
            return Results.Accepted($"/api/shell/jobs/{job.Id}", job);
        });

        api.MapGet("/shell/jobs/{id}", (string id) => Jobs.TryGetValue(id, out var job) ? Results.Ok(job) : Results.NotFound());

        api.MapPost("/shell/jobs/{id}/result", (HttpContext ctx, string id, ShellResultBody b) =>
        {
            if (!ctx.IsWindowsClient()) return Results.Json(new { error = "Only the Windows client runs commands." }, statusCode: 403);
            if (!Jobs.TryGetValue(id, out var job)) return Results.NotFound();
            job.Output = b.Output.Length > 200_000 ? b.Output[..200_000] : b.Output;
            job.Failed = b.Failed;
            job.Status = "done";
            return Results.Ok(job);
        });

        // Permission grants for agents, plugins and automations. Changing them needs approval in the Windows client.
        api.MapPut("/permissions", (VaultHost host, HttpContext ctx, GrantBody b) =>
        {
            var session = host.Require();
            var description = $"Allow {b.Kind} “{b.Name}” to: {(b.Permissions.Count == 0 ? "nothing" : string.Join(", ", b.Permissions))}";
            session.Permissions.Demand(ctx.Actor(), Permission.ManagePermissions, description, b.Approval);
            session.Permissions.Set(b.Kind, b.Name, b.Permissions);
            return Results.Ok(new { grants = session.Permissions.Current });
        });

        api.MapPost("/system/safe-mode", (VaultHost host, SafeModeBody b) =>
        {
            var session = host.Require();
            session.Permissions.SafeMode = b.On;
            var flag = Path.Combine(host.Paths.Settings, "safe-mode");
            if (b.On) File.WriteAllText(flag, "on"); else if (File.Exists(flag)) File.Delete(flag);
            return Results.Ok(new { safeMode = b.On });
        });

        api.MapGet("/automations/recent", (VaultHost host) => host.Require().Automations.Recent);

        api.MapGet("/plugins", (VaultHost host) =>
        {
            var session = host.Require();
            var enabled = EnabledPlugins(host);
            return PluginCatalog.List(session.Vault).Select(p => new
            {
                p.Name, p.Version, p.Description, p.Commands, p.Permissions, p.Error, enabled = enabled.Contains(p.Name),
                granted = session.Permissions.Current.Plugins.GetValueOrDefault(p.Name)?.ToList() ?? []
            });
        });

        api.MapPost("/plugins/enabled", (VaultHost host, PluginToggleBody b) =>
        {
            var enabled = EnabledPlugins(host);
            if (b.Enabled) enabled.Add(b.Name); else enabled.Remove(b.Name);
            Directory.CreateDirectory(host.Paths.Settings);
            File.WriteAllText(Path.Combine(host.Paths.Settings, "plugins.json"), JsonSerializer.Serialize(enabled));
            return Results.Ok(enabled);
        });

        // The sandboxed frame a plugin runs in. Its scripts carry a per-response nonce; it can't reach the API directly.
        app.MapGet("/plugin-host/{name}", (VaultHost host, HttpContext ctx, string name) =>
        {
            var session = host.Require();
            if (session.Permissions.SafeMode) return Results.NotFound();
            var script = PluginCatalog.Script(session.Vault, name);
            if (script is null) return Results.NotFound();
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
            var page = $$"""
                <!doctype html><html><head><meta charset="utf-8"></head><body>
                <script nonce="{{nonce}}">{{PluginRuntime}}</script>
                <script nonce="{{nonce}}">{{script.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase)}}</script>
                </body></html>
                """;
            ctx.Response.Headers.ContentSecurityPolicy = $"default-src 'none'; script-src 'nonce-{nonce}'; style-src 'unsafe-inline'; sandbox allow-scripts";
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Content(page, "text/html; charset=utf-8");
        });
    }


    private static HashSet<string> EnabledPlugins(VaultHost host)
    {
        var file = Path.Combine(host.Paths.Settings, "plugins.json");
        try { return File.Exists(file) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(file)) ?? [] : []; }
        catch (JsonException) { return []; }
    }

    /// <summary>The API a plugin sees: everything goes through postMessage to the app, which checks the plugin's grants.</summary>
    private const string PluginRuntime = """
        (function () {
          let next = 1; const pending = new Map(); const commands = new Map();
          function call(method, args) {
            return new Promise((resolve, reject) => {
              const id = next++; pending.set(id, { resolve, reject });
              parent.postMessage({ quantos: true, id, method, args }, '*');
            });
          }
          addEventListener('message', (e) => {
            if (e.source !== parent || !e.data || !e.data.quantos) return;
            if (e.data.command) { const fn = commands.get(e.data.command); if (fn) fn(); return; }
            const p = pending.get(e.data.id); if (!p) return; pending.delete(e.data.id);
            e.data.error ? p.reject(new Error(e.data.error)) : p.resolve(e.data.result);
          });
          window.quantos = {
            api: (method, path, body) => call('api', { method, path, body }),
            search: (q) => call('api', { method: 'GET', path: 'search?q=' + encodeURIComponent(q) }),
            getNote: (id) => call('api', { method: 'GET', path: 'nodes/' + id }),
            createNote: (title, body) => call('api', { method: 'POST', path: 'nodes', body: { title, body } }),
            toast: (message) => call('toast', { message }),
            registerCommand: (id, title, fn) => { commands.set(id, fn); return call('command', { id, title }); }
          };
        })();
        """;
}
