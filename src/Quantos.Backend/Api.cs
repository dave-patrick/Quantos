using System.Globalization;
using Quantos.Core;

namespace Quantos.Backend;

/// <summary>
/// HTTP API v1 for the shared frontend, the Windows client and (later) the CLI.
/// All routes require the access token (see <see cref="LocalSecurity"/>).
/// </summary>
public static class Api
{
    public sealed record CreateNodeRequest(string? Workspace, string Title, string? Body);
    public sealed record UpdateNodeRequest(string ExpectedVersion, string? Title, string? Body);
    public sealed record TrashRequest(string ExpectedVersion);

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/status", (WriteService writes, BackendOptions options) => new
        {
            app = "Quantos",
            version = typeof(Api).Assembly.GetName().Version?.ToString(3),
            vault = options.VaultPath,
            warnings = VaultLocation.Warnings(options.VaultPath),
            workspaces = writes.Layout.ListWorkspaces(),
            diagnostics = writes.Diagnostics(),
        });

        api.MapGet("/workspaces/{workspace}/nodes", (string workspace, WriteService writes) =>
        {
            VaultLayout.ValidateWorkspaceName(workspace);
            return writes.List(workspace);
        });

        api.MapPost("/nodes", async (CreateNodeRequest req, WriteService writes) =>
        {
            var node = await writes.CreateAsync(req.Workspace ?? VaultLayout.DefaultWorkspace, req.Title, req.Body ?? "");
            return Results.Created($"/api/nodes/{node.Header.Id}", node);
        });

        api.MapGet("/nodes/{id}", (string id, WriteService writes) => writes.Read(id));

        api.MapPut("/nodes/{id}", (string id, UpdateNodeRequest req, WriteService writes) =>
            writes.UpdateAsync(id, req.ExpectedVersion, req.Title, req.Body));

        api.MapPost("/nodes/{id}/trash", (string id, TrashRequest req, WriteService writes) =>
            writes.TrashAsync(id, req.ExpectedVersion));

        api.MapGet("/nodes/{id}/history", (string id, WriteService writes) => writes.History(id));

        api.MapGet("/nodes/{id}/history/{entry}", (string id, string entry, WriteService writes) =>
            Results.Text(writes.ReadHistory(id, entry), "text/markdown; charset=utf-8"));

        api.MapGet("/daily/{date}", async (string date, string? workspace, WriteService writes) =>
        {
            var day = date == "today"
                ? DateOnly.FromDateTime(DateTime.Now)
                : DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? d
                    : throw new ValidationException("Use a date like 2026-10-15, or 'today'.");
            return await writes.GetOrCreateDailyAsync(workspace ?? VaultLayout.DefaultWorkspace, day);
        });

        api.MapGet("/trash", (WriteService writes) => writes.ListTrash());

        api.MapPost("/trash/{trashId}/restore", (string trashId, WriteService writes) => writes.RestoreAsync(trashId));

        api.MapPost("/cache/rebuild", (WriteService writes) => writes.RebuildCacheAsync());

        // Safe shutdown: waits for in-flight saves (the write gate) before the process exits.
        api.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                lifetime.StopApplication();
            });
            return Results.Accepted();
        });
    }
}
