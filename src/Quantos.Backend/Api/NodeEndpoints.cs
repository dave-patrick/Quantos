using System.Globalization;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Storage;

namespace Quantos.Backend.Api;

public static class NodeEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/nodes", (VaultHost host, string? workspace, string? type, bool? archived) =>
            host.Require().Cache.List(workspace, type, archived ?? false));

        api.MapGet("/nodes/{id}", (VaultHost host, string id) =>
            host.Require().Writes.Get(id) is { } node ? Results.Ok(NodeDto.From(node)) : Results.NotFound(new { error = "Not found." }));

        api.MapPost("/nodes", (VaultHost host, HttpContext ctx, CreateNodeBody body) =>
        {
            var node = host.Require().Writes.Create(new CreateNodeRequest
            {
                Title = body.Title,
                Type = body.Type ?? NodeTypes.Note,
                Workspace = body.Workspace ?? Vault.DefaultWorkspace,
                Body = body.Body ?? "",
                Tags = body.Tags,
                Properties = Json.ToClr(body.Properties),
                Folder = body.Folder,
                Date = body.Date is null ? null : DateOnly.Parse(body.Date, CultureInfo.InvariantCulture)
            }, ctx.Actor());
            return Results.Ok(NodeDto.From(node));
        });

        api.MapPut("/nodes/{id}", (VaultHost host, HttpContext ctx, string id, SaveBody body) =>
            ToResult(host.Require().Writes.Save(id, body.Content, body.BaseHash, ctx.Actor())));

        api.MapPatch("/nodes/{id}", (VaultHost host, HttpContext ctx, string id, PatchBody body) =>
        {
            var session = host.Require();
            var set = Json.ToClr(body.Set);
            var baseHash = body.BaseHash;
            // A task's status always goes through the task service: completion time, recurrence and
            // inline checkboxes stay consistent however the change is made (board, table, form).
            if (set is not null && set.TryGetValue("status", out var status) && session.Writes.Get(id) is { Type: NodeTypes.Task } task)
            {
                if (baseHash is not null && baseHash != task.Hash) return ToResult(new SaveResult(SaveStatus.Conflict, null, task.Content, task.Hash, "The file changed on disk since you opened it."));
                session.Tasks.SetStatus(id, Core.Tasks.Props.Str(status) ?? "todo", ctx.Actor());
                set.Remove("status");
                baseHash = null;
                if (set.Count == 0 && (body.Remove?.Count ?? 0) == 0 && body.Body is null)
                    return ToResult(new SaveResult(SaveStatus.Saved, session.Writes.Require(id)));
            }
            return ToResult(session.Writes.UpdateFields(id, baseHash, set, body.Remove, ctx.Actor(), body.Body));
        });

        api.MapPost("/nodes/{id}/rename", (VaultHost host, HttpContext ctx, string id, RenameBody body) =>
            NodeDto.From(host.Require().Writes.Rename(id, body.Title, ctx.Actor())));

        api.MapPost("/nodes/{id}/move", (VaultHost host, HttpContext ctx, string id, MoveBody body) =>
        {
            var writes = host.Require().Writes;
            var node = body.Workspace is { Length: > 0 } ws ? writes.MoveToWorkspace(id, ws, ctx.Actor())
                : body.Folder is { Length: > 0 } folder ? writes.MoveToFolder(id, folder, ctx.Actor(), body.Rename ?? false)
                : throw new QuantosException("Give a workspace or a folder.");
            return NodeDto.From(node);
        });

        api.MapPost("/nodes/{id}/archive", (VaultHost host, HttpContext ctx, string id, ArchiveBody body) =>
            NodeDto.From(host.Require().Writes.SetArchived(id, body.Archived, ctx.Actor())));

        api.MapPost("/nodes/{id}/trash", (VaultHost host, HttpContext ctx, string id) =>
            host.Require().Writes.Trash(id, ctx.Actor()));

        api.MapGet("/nodes/{id}/backlinks", (VaultHost host, string id) => host.Require().Cache.Backlinks(id));
        api.MapGet("/nodes/{id}/outlinks", (VaultHost host, string id) => host.Require().Cache.OutLinks(id));
        api.MapGet("/nodes/{id}/history", (VaultHost host, string id) => host.Require().Writes.History.List(id));

        api.MapGet("/nodes/{id}/history/{version}", (VaultHost host, string id, string version) =>
            host.Require().Writes.History.Read(id, version) is { } content ? Results.Ok(new { version, content }) : Results.NotFound());

        api.MapPost("/nodes/{id}/rollback", (VaultHost host, HttpContext ctx, string id, RollbackBody body) =>
            NodeDto.From(host.Require().Writes.Rollback(id, body.Version, ctx.Actor())));

        api.MapGet("/resolve", (VaultHost host, string title, string? workspace) =>
            host.Require().Cache.FindByTitle(title, workspace));

        api.MapGet("/search", (VaultHost host, string q, string? workspace, int? limit) =>
            host.Require().Cache.Search(q, workspace, Math.Clamp(limit ?? 50, 1, 500)));

        // Trash
        api.MapGet("/trash", (VaultHost host) => host.Require().Writes.TrashBin.List());

        api.MapPost("/trash/{entry}/restore", (VaultHost host, HttpContext ctx, string entry) =>
            NodeDto.From(host.Require().Writes.Restore(entry, ctx.Actor())));

        api.MapDelete("/trash/{entry}", (VaultHost host, HttpContext ctx, string entry, string? approval) =>
        {
            host.Require().Writes.DeletePermanently(entry, ctx.Actor(), approval);
            return Results.NoContent();
        });

        // Workspaces and folders
        api.MapGet("/workspaces", (VaultHost host) => host.Require().Vault.WorkspaceNames());

        api.MapPost("/workspaces", (VaultHost host, HttpContext ctx, WorkspaceBody body) =>
        {
            var session = host.Require();
            session.Permissions.Demand(ctx.Actor(), Core.Security.Permission.CreateEdit, $"Create workspace {body.Name}");
            session.Vault.EnsureWorkspace(Vault.ValidateWorkspaceName(body.Name));
            return session.Vault.WorkspaceNames();
        });

        api.MapPost("/folders", (VaultHost host, HttpContext ctx, FolderBody body) =>
        {
            var session = host.Require();
            session.Permissions.Demand(ctx.Actor(), Core.Security.Permission.CreateEdit, $"Create folder {body.Path}");
            var abs = session.Vault.Absolute(body.Path);
            if (Vault.WorkspaceOf(session.Vault.Relative(abs) + "/x") is not { Length: > 0 })
                throw new QuantosException("Folders can only be created inside a workspace.");
            Directory.CreateDirectory(abs);
            return Results.Ok(new { path = session.Vault.Relative(abs) });
        });

        api.MapGet("/tree", (VaultHost host, string workspace, bool? archived) => Tree(host.Require(), workspace, archived ?? false));

        // Daily notes
        api.MapGet("/daily/{workspace}/{date}", (VaultHost host, string workspace, string date) =>
        {
            var session = host.Require();
            var day = DateOnly.Parse(date, CultureInfo.InvariantCulture);
            var node = session.Writes.GetByPath(session.Vault.Relative(session.Vault.DailyPath(workspace, day)));
            return node is null ? Results.NotFound() : Results.Ok(NodeDto.From(node));
        });

        api.MapPost("/daily/{workspace}/{date}", (VaultHost host, HttpContext ctx, string workspace, string date) =>
            NodeDto.From(host.Require().Writes.GetOrCreateDaily(workspace, DateOnly.Parse(date, CultureInfo.InvariantCulture), ctx.Actor())));

        api.MapGet("/daily-stream/{workspace}", (VaultHost host, string workspace, string? from, string? to) =>
        {
            var session = host.Require();
            var end = to is null ? session.Clock.Today : DateOnly.Parse(to, CultureInfo.InvariantCulture);
            var start = from is null ? end.AddDays(-13) : DateOnly.Parse(from, CultureInfo.InvariantCulture);
            if (end.DayNumber - start.DayNumber > 92) start = end.AddDays(-92);
            var days = new List<object>();
            for (var day = end; day >= start; day = day.AddDays(-1))
            {
                var node = session.Writes.GetByPath(session.Vault.Relative(session.Vault.DailyPath(workspace, day)));
                days.Add(new { date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), node = node is null ? null : NodeDto.From(node) });
            }
            return days;
        });
    }

    private static IResult ToResult(SaveResult result) => result.Status switch
    {
        SaveStatus.Conflict => Results.Conflict(SaveResultDto.From(result)),
        SaveStatus.NotFound => Results.NotFound(SaveResultDto.From(result)),
        SaveStatus.Invalid or SaveStatus.ReadOnly => Results.UnprocessableEntity(SaveResultDto.From(result)),
        _ => Results.Ok(SaveResultDto.From(result))
    };

    private static TreeFolder Tree(VaultSession session, string workspace, bool includeArchived)
    {
        var root = session.Vault.WorkspaceDir(workspace);
        var nodes = session.Cache.List(workspace, null, includeArchived: true, limit: 100_000)
            .Where(n => includeArchived || !Vault.IsArchivedPath(n.Path))
            .GroupBy(n => n.Path[..n.Path.LastIndexOf('/')], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        TreeFolder Build(string dir)
        {
            var rel = session.Vault.Relative(dir);
            var folders = Directory.Exists(dir)
                ? Directory.EnumerateDirectories(dir)
                    .Where(d => includeArchived || !string.Equals(Path.GetFileName(d), "archive", StringComparison.OrdinalIgnoreCase) || !Vault.PathEquals(Path.GetDirectoryName(d), root))
                    .Order(StringComparer.OrdinalIgnoreCase).Select(Build).ToList()
                : [];
            var files = nodes.TryGetValue(rel, out var list)
                ? list.OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase).Select(n => new TreeNode(n.Id, n.Title, n.Type, n.Path, n.Modified, n.Archived)).ToList()
                : [];
            return new TreeFolder(Path.GetFileName(dir), rel, folders, files);
        }

        return Build(root);
    }
}
