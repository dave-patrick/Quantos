using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Backups;
using Quantos.Core.Security;

namespace Quantos.Backend.Api;

public sealed record BackupNoteBody(string Path);
public sealed record BackupRestoreBody(string? Folder);

/// <summary>Rolling backups (Release 1.1): status, settings, back up now, verify and restore.</summary>
public static class BackupEndpoints
{
    /// <summary>Where backups go and restores land are the person's choice, not an agent's or plugin's.</summary>
    private static void PersonOnly(HttpContext ctx)
    {
        if (ctx.Actor().Kind is not (ActorKind.User or ActorKind.Cli)) throw new QuantosException("Only you can manage backups.", 403);
    }

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/backups");

        api.MapGet("/", (VaultHost host) => host.Require().Backups.Status());

        api.MapPut("/settings", (VaultHost host, HttpContext ctx, BackupSettings body) =>
        {
            PersonOnly(ctx);
            host.Require().Backups.SaveSettings(body);
            return host.Require().Backups.Status();
        });

        api.MapPost("/", async (VaultHost host, HttpContext ctx) =>
        {
            PersonOnly(ctx);
            return await host.Require().Backups.BackUpAsync(ctx.Actor(), ctx.RequestAborted);
        });

        api.MapPost("/{name}/verify", (VaultHost host, HttpContext ctx, string name) =>
            new { problems = host.Require().Backups.Verify(name, ctx.Actor()) });

        api.MapGet("/{name}/notes", (VaultHost host, HttpContext ctx, string name) =>
            host.Require().Backups.Notes(name, ctx.Actor()));

        api.MapPost("/{name}/restore-note", (VaultHost host, HttpContext ctx, string name, BackupNoteBody body) =>
        {
            PersonOnly(ctx);
            return NodeDto.From(host.Require().Backups.RestoreNote(name, body.Path, ctx.Actor()));
        });

        api.MapPost("/{name}/restore", (VaultHost host, HttpContext ctx, string name, BackupRestoreBody body) =>
        {
            PersonOnly(ctx);
            return new { folder = host.Require().Backups.RestoreToFolder(name, body.Folder, ctx.Actor()) };
        });
    }
}
