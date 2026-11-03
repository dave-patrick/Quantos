using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Security;

namespace Quantos.Backend.Api;

public static class GoogleTasksEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/google-tasks");
        api.AddEndpointFilter(async (context, next) =>
        {
            if (context.HttpContext.Actor().Kind is not ActorKind.User) throw new QuantosException("Only you can configure Google Tasks sync.", 403);
            return await next(context);
        });
        api.MapGet("/", (GoogleTasksCoordinator g) => g.Status());
        api.MapPut("/settings", async (GoogleTasksCoordinator g, GoogleSettingsBody body, CancellationToken ct) => { await g.Configure(body, ct); return g.Status(); });
        api.MapPost("/connect", (GoogleTasksCoordinator g) => g.Connect());
        api.MapGet("/lists", async (GoogleTasksCoordinator g, CancellationToken ct) => (await g.Lists(ct)).Select(l => new { id = l.Id, title = l.Title }));
        api.MapPost("/lists", async (GoogleTasksCoordinator g, GoogleListBody body, CancellationToken ct) =>
        {
            var list = await g.CreateList(body.Title, ct);
            return new { id = list.Id, title = list.Title };
        });
        api.MapPost("/preview", async (GoogleTasksCoordinator g, GoogleWorkspaceBody body, CancellationToken ct) =>
        {
            var p = await g.Preview(body.Workspace, ct);
            return new { cutoff = p.Cutoff, createInGoogle = p.CreateInGoogle, importToQuantos = p.ImportToQuantos, link = p.Link, excluded = p.Excluded };
        });
        api.MapPost("/confirm", (GoogleTasksCoordinator g, GoogleConfirmBody body) => { g.Confirm(body.Workspace, body.Cutoff); return g.Status(); });
        api.MapPost("/sync", async (GoogleTasksCoordinator g, CancellationToken ct) =>
            await g.SyncNow(ct) is { } report ? Results.Ok(report) : Results.Conflict(new { error = "The sync was stopped because the settings changed. Try again." }));
        api.MapPost("/deletes", async (GoogleTasksCoordinator g, GoogleDeletesBody body, CancellationToken ct) => { await g.ResolveDeletes(body.Action, ct); return g.Status(); });
        api.MapDelete("/", async (GoogleTasksCoordinator g, CancellationToken ct) => { await g.Disconnect(ct); return Results.NoContent(); });
    }
}
