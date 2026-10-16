using System.Text.Json;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Storage;
using Quantos.Core.Tasks;
using TaskStatus = Quantos.Core.Tasks.TaskStatus;

namespace Quantos.Backend.Api;

public sealed record TaskCreateBody(string Title, string? Workspace, string? Body, string? Due, string? Planned, int? Priority, int? Effort,
    string? Parent, string? Project, List<string>? Assignees, string? Source, string? Recurrence, List<string>? DependsOn, List<string>? Tags,
    List<string>? Reminders, string? Status, string? WaitingOn, string? Stage);

public sealed record StatusBody(string Status);
public sealed record TaskUpdateBody(Dictionary<string, JsonElement>? Set, List<string>? Remove);
public sealed record SnoozeBody(int Minutes);
public sealed record RolloverBody(string Workspace, string? Today);

public static class TaskEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/tasks");

        api.MapGet("", (VaultHost host, string? workspace, string? include) =>
            host.Require().Tasks.List(workspace, includeClosed: include is "all" or null, includeArchived: include == "all"));

        api.MapPost("", (VaultHost host, HttpContext ctx, TaskCreateBody b) => NodeDto.From(host.Require().Tasks.Create(new CreateTaskRequest
        {
            Title = b.Title, Workspace = b.Workspace ?? Vault.DefaultWorkspace, Body = b.Body ?? "", Due = b.Due, Planned = b.Planned, Priority = b.Priority,
            Effort = b.Effort, Parent = b.Parent, Project = b.Project, Assignees = b.Assignees, Source = b.Source, Recurrence = b.Recurrence,
            DependsOn = b.DependsOn, Tags = b.Tags, Reminders = b.Reminders, Status = b.Status ?? TaskStatus.Todo, WaitingOn = b.WaitingOn, Stage = b.Stage
        }, ctx.Actor())));

        api.MapGet("/{id}", (VaultHost host, string id) => host.Require().Tasks.Get(id) is { } t ? Results.Ok(t) : Results.NotFound());

        api.MapPost("/{id}/status", (VaultHost host, HttpContext ctx, string id, StatusBody b) => host.Require().Tasks.SetStatus(id, b.Status, ctx.Actor()));

        api.MapPost("/{id}/update", (VaultHost host, HttpContext ctx, string id, TaskUpdateBody b) =>
            host.Require().Tasks.Update(id, Json.ToClr(b.Set) ?? new(), b.Remove ?? [], ctx.Actor()));

        api.MapPost("/{id}/snooze", (VaultHost host, HttpContext ctx, string id, SnoozeBody b) =>
            host.Require().Tasks.Snooze(id, TimeSpan.FromMinutes(Math.Clamp(b.Minutes, 1, 60 * 24 * 30)), ctx.Actor()));

        api.MapPost("/rollover", (VaultHost host, HttpContext ctx, RolloverBody b) =>
        {
            var session = host.Require();
            var today = b.Today is null ? session.Clock.Today : DateOnly.Parse(b.Today);
            return new { moved = session.Tasks.Rollover(b.Workspace, today, ctx.Actor()) };
        });

        api.MapGet("/calendar.ics", (VaultHost host, string? workspace) =>
        {
            var session = host.Require();
            var ics = Calendar.Build(session.Tasks.List(workspace), "Quantos" + (workspace is null ? "" : " - " + workspace));
            return Results.Text(ics, "text/calendar; charset=utf-8");
        });

        api.MapPost("/calendar", (VaultHost host, HttpContext ctx, WorkspaceBody b) =>
        {
            var session = host.Require();
            session.Permissions.Demand(ctx.Actor(), Core.Security.Permission.CreateEdit, "Export calendar");
            var path = Path.Combine(session.Vault.Exports, $"quantos-{b.Name}.ics");
            AtomicFile.WriteAllText(path, Calendar.Build(session.Tasks.List(b.Name), "Quantos - " + b.Name));
            return new { path };
        });
    }
}
