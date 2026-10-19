using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Updates;

namespace Quantos.Backend.Api;

/// <summary>Updates: status, check, download (the Windows client installs). Only the person can trigger network use.</summary>
public static class UpdateEndpoints
{
    private static void PersonOnly(HttpContext ctx)
    {
        if (ctx.Actor().Kind is not (ActorKind.User or ActorKind.Cli)) throw new QuantosException("Only you can check for and download updates.", 403);
    }

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/updates");
        api.MapGet("/", (UpdateService updates) => updates.Status());
        api.MapPost("/check", async (UpdateService updates, HttpContext ctx) => { PersonOnly(ctx); return await updates.CheckAsync(ctx.RequestAborted); });
        // The download keeps going if the page is closed meanwhile; the status shows its progress.
        api.MapPost("/download", (UpdateService updates, HttpContext ctx) =>
        {
            PersonOnly(ctx);
            _ = Task.Run(async () => { try { await updates.DownloadAsync(); } catch (QuantosException) { /* the status carries the reason */ } });
            return updates.Status();
        });
        api.MapPut("/settings", (UpdateService updates, HttpContext ctx, UpdateSettings body) => { PersonOnly(ctx); updates.SaveSettings(body); return updates.Status(); });
    }
}
