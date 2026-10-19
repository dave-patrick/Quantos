using Quantos.Backend.Hosting;

namespace Quantos.Backend.Api;

public sealed record FocusBody(string? Workspace, DateTimeOffset Start, DateTimeOffset End, string? TaskId, string? Label);

/// <summary>Focus sessions (Release 1.1): log a finished session, and read the totals back.</summary>
public static class FocusEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/focus");

        api.MapGet("/", (VaultHost host, HttpContext ctx, string? workspace, int? days) =>
            host.Require().Focus.Summary(workspace ?? "default", days ?? 7, ctx.Actor()));

        api.MapPost("/", (VaultHost host, HttpContext ctx, FocusBody body) =>
            host.Require().Focus.Log(body.Workspace ?? "default", body.Start, body.End, body.TaskId, body.Label, ctx.Actor()));
    }
}
