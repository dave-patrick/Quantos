using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Security;

namespace Quantos.Backend.Api;

public sealed record SyncResolutionBody(bool KeepLocal);
public sealed record ServerMcpBody(bool WriteAccess);
public static class SyncEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/sync");
        api.AddEndpointFilter(async (context, next) =>
        {
            if (context.HttpContext.Actor().Kind is not ActorKind.User) throw new QuantosException("Only you can configure shared-vault sync.", 403);
            return await next(context);
        });
        api.MapGet("/", (SyncCoordinator sync) => sync.Status());
        api.MapGet("/mcp", async (SyncCoordinator sync, CancellationToken ct) => await sync.McpSettings(null, ct));
        api.MapPut("/mcp", async (SyncCoordinator sync, ServerMcpBody body, CancellationToken ct) => await sync.McpSettings(body.WriteAccess, ct));
        api.MapPost("/mcp/connection", async (SyncCoordinator sync, CancellationToken ct) => await sync.McpConnection(ct));
        api.MapPut("/", async (SyncCoordinator sync, SyncConnectionBody body, CancellationToken ct) => { await sync.Configure(body, ct); return sync.Status(); });
        api.MapDelete("/", async (SyncCoordinator sync, CancellationToken ct) => { await sync.Disconnect(ct); return Results.NoContent(); });
        api.MapPost("/run", async (SyncCoordinator sync, CancellationToken ct) => await sync.SyncNow(ct));
        api.MapPost("/conflicts/{id}", async (SyncCoordinator sync, string id, SyncResolutionBody body, CancellationToken ct) => { await sync.Resolve(id, body.KeepLocal, ct); return sync.Status(); });
        api.MapGet("/conflicts/{id}/version", (SyncCoordinator sync, string id, bool local) =>
            sync.Version(id, local) is { } bytes ? Results.File(bytes, "application/octet-stream", "Quantos-conflict-" + (local ? "device" : "server")) : Results.NotFound());
    }
}
