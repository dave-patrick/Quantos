using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Security;

namespace Quantos.Backend.Api;

public static class FolderBridgeEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/folder-bridges");
        api.AddEndpointFilter(async (context, next) =>
        {
            if (context.HttpContext.Actor().Kind is not ActorKind.User) throw new QuantosException("Only you can manage folder bridges.", 403);
            return await next(context);
        });
        api.MapGet("/", (FolderBridgeCoordinator bridges) => bridges.Status());
        api.MapPost("/", async (FolderBridgeCoordinator bridges, FolderBridgeBody body, CancellationToken ct) => { await bridges.Add(body, ct); return bridges.Status(); });
        api.MapPost("/{id}/start", async (FolderBridgeCoordinator bridges, string id, CancellationToken ct) => { await bridges.Start(id, ct); return bridges.Status(); });
        api.MapPost("/{id}/stop", async (FolderBridgeCoordinator bridges, string id, CancellationToken ct) => { await bridges.Stop(id, false, ct); return bridges.Status(); });
        api.MapDelete("/{id}", async (FolderBridgeCoordinator bridges, string id, CancellationToken ct) => { await bridges.Stop(id, true, ct); return bridges.Status(); });
    }
}
