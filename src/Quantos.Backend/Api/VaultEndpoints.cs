using System.Text.Json.Nodes;
using Quantos.Backend.Hosting;
using Quantos.Core;

namespace Quantos.Backend.Api;

public sealed record CaptureBody(string Text, string? Workspace, string? Target);
public sealed record PreserveBody(string Content, string? Label);
public sealed record ReadBody(List<string>? Ids);
public sealed record LayoutBody(JsonNode? Layout);
public sealed record ExportBody(string? Format, string? Path);
public sealed record TableExportBody(string? Name, string? Csv);

public static class VaultEndpoints
{
    private static async Task<Stream> Buffer(Stream s)
    {
        var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        ms.Position = 0;
        return ms;
    }

    private static Core.Export.ExportFormat Format(string? f) => f?.ToLowerInvariant() switch
    {
        "html" => Core.Export.ExportFormat.Html, "pdf" => Core.Export.ExportFormat.Pdf, _ => Core.Export.ExportFormat.Markdown
    };

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/config/{name}", (VaultHost host, string name) => Results.Json(host.Require().Config.Read(name)));

        api.MapPut("/config/{name}", (VaultHost host, HttpContext ctx, string name, JsonNode? body) =>
        {
            host.Require().Config.Write(name, body, ctx.Actor());
            return Results.NoContent();
        });

        // Custom styling (Release 1.1): .css files in the vault's themes folder. The page's content
        // security policy keeps them local (no remote images, fonts or imports reach the network).
        api.MapGet("/themes", (VaultHost host) =>
        {
            var dir = host.Require().Vault.Themes;
            if (!Directory.Exists(dir)) return Results.Ok(Array.Empty<object>());
            return Results.Ok(Directory.EnumerateFiles(dir, "*.css").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => new FileInfo(f)).Select(f => new { name = f.Name, size = f.Length, modified = f.LastWriteTimeUtc }));
        });

        api.MapGet("/themes/{name}", (VaultHost host, string name) =>
        {
            // A plain file name in the themes folder only: no folders, no other extensions.
            if (name != Path.GetFileName(name) || !name.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
            var path = Path.Combine(host.Require().Vault.Themes, name);
            return File.Exists(path) ? Results.Text(Core.Storage.AtomicFile.ReadAllText(path), "text/css") : Results.NotFound();
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

        api.MapPost("/export/node/{id}", (VaultHost host, HttpContext ctx, string id, ExportBody b) =>
            host.Require().Exports.ExportNode(id, Format(b.Format), ctx.Actor()));

        api.MapPost("/export/table", (VaultHost host, HttpContext ctx, TableExportBody b) =>
            host.Require().Exports.ExportTable(b.Name ?? "table", b.Csv ?? "", ctx.Actor()));

        api.MapPost("/export/folder", (VaultHost host, HttpContext ctx, ExportBody b) =>
            host.Require().Exports.ExportFolder(b.Path ?? throw new QuantosException("Choose a folder."), Format(b.Format), ctx.Actor()));

        // Attachments (images in notes, PDFs): served read-only from the vault's attachments folder.
        api.MapGet("/files/{**path}", (VaultHost host, string path) =>
        {
            var session = host.Require();
            var abs = session.Vault.Absolute(path);
            if (!abs.StartsWith(session.Vault.Attachments, StringComparison.OrdinalIgnoreCase) || !File.Exists(abs)) return Results.NotFound();
            var type = Path.GetExtension(abs).ToLowerInvariant() switch
            {
                ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp", ".svg" => "image/svg+xml",
                ".pdf" => "application/pdf", ".txt" or ".md" => "text/plain; charset=utf-8", _ => "application/octet-stream"
            };
            return Results.File(abs, type, enableRangeProcessing: true);
        });

        api.MapPost("/attachments", async (VaultHost host, HttpContext ctx, string name) =>
        {
            var (path, existed) = host.Require().Attachments.Add(ctx.Request.Body is { } s ? await Buffer(s) : Stream.Null, name, ctx.Actor());
            var isImage = Path.GetExtension(path) is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg";
            return new { path, existed, markdown = (isImage ? "!" : "") + $"[{name.Replace("]", "")}]({path})" };
        });
        api.MapGet("/attachments/audit", (VaultHost host) => host.Require().Attachments.Audit());
        api.MapPost("/attachments/trash", (VaultHost host, HttpContext ctx, ExportBody b) =>
            host.Require().Attachments.TrashOrphan(b.Path ?? "", ctx.Actor()));

        api.MapGet("/notifications", (VaultHost host) => host.Require().Notifications.List());

        api.MapPost("/notifications/read", (VaultHost host, ReadBody b) =>
        {
            host.Require().Notifications.MarkRead(b.Ids ?? []);
            return Results.NoContent();
        });
    }
}
