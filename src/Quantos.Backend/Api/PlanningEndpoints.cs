using Quantos.Backend.Hosting;
using Quantos.Core.Tasks;

namespace Quantos.Backend.Api;

public sealed record CarryBody(string? To);

/// <summary>Sprints, goal rollups and task analytics (Release 1.1).</summary>
public static class PlanningEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/plan");

        api.MapGet("/sprints", (VaultHost host, HttpContext ctx, string? workspace) => host.Require().Planning.Sprints(workspace ?? "default", ctx.Actor()));
        api.MapGet("/sprints/{id}", (VaultHost host, HttpContext ctx, string id) => host.Require().Planning.SprintDetail(id, ctx.Actor()));
        api.MapPost("/sprints", (VaultHost host, HttpContext ctx, CreateSprintRequest body) => NodeDto.From(host.Require().Planning.CreateSprint(body, ctx.Actor())));
        api.MapPost("/sprints/{id}/carry", (VaultHost host, HttpContext ctx, string id, CarryBody body) => new { moved = host.Require().Planning.CarryOver(id, body.To, ctx.Actor()) });
        api.MapGet("/backlog", (VaultHost host, HttpContext ctx, string? workspace) => host.Require().Planning.Backlog(workspace ?? "default", ctx.Actor()));
        api.MapGet("/goals", (VaultHost host, HttpContext ctx, string? workspace) => host.Require().Planning.Goals(workspace ?? "default", ctx.Actor()));
        api.MapGet("/analytics", (VaultHost host, HttpContext ctx, string? workspace, int? weeks) => host.Require().Planning.Analytics(workspace ?? "default", weeks ?? 8, ctx.Actor()));
    }
}
