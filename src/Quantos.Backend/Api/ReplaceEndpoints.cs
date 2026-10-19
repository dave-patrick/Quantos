using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Structure;

namespace Quantos.Backend.Api;

/// <summary>Batch find and replace (Release 1.1): preview, apply, undo.</summary>
public static class ReplaceEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/replace");

        api.MapPost("/preview", (VaultHost host, HttpContext ctx, ReplaceQuery body) =>
            host.Require().Replace.Preview(body, ctx.Actor()));

        // Changing many notes at once is the person's call: agents and plugins edit one note at a time.
        api.MapPost("/apply", (VaultHost host, HttpContext ctx, ReplaceQuery body) =>
        {
            if (ctx.Actor().Kind is not (ActorKind.User or ActorKind.Cli)) throw new QuantosException("Only you can replace across notes.", 403);
            return host.Require().Replace.Apply(body, ctx.Actor());
        });

        api.MapPost("/undo/{id}", (VaultHost host, HttpContext ctx, string id) =>
        {
            var (restored, kept) = host.Require().Replace.Undo(id, ctx.Actor());
            return new { restored, kept };
        });
    }
}
