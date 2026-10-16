using System.Text.Json.Nodes;
using Quantos.Backend.Hosting;
using Quantos.Core;

namespace Quantos.Backend.Api;

public sealed record CaptureBody(string Text, string? Workspace, string? Target);
public sealed record PreserveBody(string Content, string? Label);
public sealed record ReadBody(List<string>? Ids);
public sealed record LayoutBody(JsonNode? Layout);

public static class VaultEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/config/{name}", (VaultHost host, string name) => Results.Json(host.Require().Config.Read(name)));

        api.MapPut("/config/{name}", (VaultHost host, HttpContext ctx, string name, JsonNode? body) =>
        {
            host.Require().Config.Write(name, body, ctx.Actor());
            return Results.NoContent();
        });

        api.MapGet("/workspace-state/{workspace}", (VaultHost host, string workspace) =>
            Results.Json(host.Require().Config.ReadWorkspaceState(workspace) ?? new JsonObject()));

        api.MapPut("/workspace-state/{workspace}", (VaultHost host, HttpContext ctx, string workspace, JsonNode? body) =>
        {
            host.Require().Config.WriteWorkspaceState(workspace, body, ctx.Actor());
            return Results.NoContent();
        });

        api.MapPost("/capture", (VaultHost host, HttpContext ctx, CaptureBody b) =>
        {
            var session = host.Require();
            var target = b.Target?.ToLowerInvariant() switch { "inbox" => CaptureTarget.Inbox, "task" => CaptureTarget.Task, _ => CaptureTarget.Daily };
            var workspace = b.Workspace is { Length: > 0 } w ? w : host.Settings.LastWorkspace ?? Core.Storage.Vault.DefaultWorkspace;
            return NodeDto.From(session.Capture.Capture(b.Text, workspace, target, ctx.Actor()));
        });

        api.MapPost("/nodes/{id}/preserve", (VaultHost host, HttpContext ctx, string id, PreserveBody b) =>
            new { version = host.Require().Writes.PreserveCopy(id, b.Content, ctx.Actor()) });

        api.MapGet("/diagnostics/integrity", (VaultHost host) =>
        {
            var (diagnostics, checkedFiles) = Integrity.Check(host.Require().Writes);
            return new { diagnostics, @checked = checkedFiles };
        });

        api.MapGet("/notifications", (VaultHost host) => host.Require().Notifications.List());

        api.MapPost("/notifications/read", (VaultHost host, ReadBody b) =>
        {
            host.Require().Notifications.MarkRead(b.Ids ?? []);
            return Results.NoContent();
        });
    }
}
