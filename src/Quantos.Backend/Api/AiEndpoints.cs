using System.Text.Json;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Ai;
using Quantos.Core.Security;

namespace Quantos.Backend.Api;

public sealed record AiKeyBody(string Provider, string? Key);
public sealed record AiModelsBody(string Provider, string? BaseUrl);
public sealed record AiAskBody(string Question, string? Workspace);
public sealed record AiTransformBody(string Instruction, string Text, string? Title);

/// <summary>Release 1.1 AI: providers, settings and keys, meaning search, and streamed answers.</summary>
public static class AiEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Keys and provider choices are the person's own: agents, plugins and automations can't touch them.</summary>
    private static void PersonOnly(HttpContext ctx)
    {
        if (ctx.Actor().Kind is not (ActorKind.User or ActorKind.Cli)) throw new QuantosException("Only you can change AI settings.", 403);
    }

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/ai");

        api.MapGet("/providers", () => AiProviders.Presets);

        api.MapGet("/settings", (VaultHost host) =>
        {
            var ai = host.Require().Ai;
            return new
            {
                settings = ai.Settings.Load(),
                keys = AiProviders.Presets.Where(p => ai.Settings.HasKey(p.Id)).Select(p => p.Id),
                index = ai.Index.Status()
            };
        });

        api.MapPut("/settings", (VaultHost host, HttpContext ctx, AiSettings body) =>
        {
            PersonOnly(ctx);
            foreach (var e in new[] { body.Chat, body.Embeddings })
                if (e is not null && AiProviders.Find(e.Provider) is null) throw new QuantosException($"Unknown provider '{e.Provider}'.");
            if (body.Embeddings?.Api == AiApi.Anthropic) throw new QuantosException("Claude has no embedding model. Choose another provider for meaning search.");
            host.Require().Ai.SaveSettings(body);
            return Results.NoContent();
        });

        api.MapPut("/key", (VaultHost host, HttpContext ctx, AiKeyBody body) =>
        {
            PersonOnly(ctx);
            if (AiProviders.Find(body.Provider) is null) throw new QuantosException($"Unknown provider '{body.Provider}'.");
            host.Require().Ai.Settings.SetKey(body.Provider, body.Key);
            host.Require().Ai.Index.Refresh();
            return Results.NoContent();
        });

        api.MapPost("/models", async (VaultHost host, HttpContext ctx, AiModelsBody body) =>
        {
            PersonOnly(ctx);
            var ai = host.Require().Ai;
            var endpoint = new AiEndpoint(body.Provider, body.BaseUrl, null);
            return await ai.Client.ListModelsAsync(endpoint, ai.Settings.GetKey(body.Provider), ctx.RequestAborted);
        });

        api.MapGet("/search", async (VaultHost host, HttpContext ctx, string q, string? workspace, int? limit) =>
        {
            var session = host.Require();
            var actor = ctx.Actor();
            session.Permissions.Demand(actor, Permission.Read, "Search notes by meaning");
            if (actor.Kind is not (ActorKind.User or ActorKind.Cli)) session.Permissions.Demand(actor, Permission.Network, "Use the AI provider");
            if (!session.Ai.Settings.Load().Enabled) throw new QuantosException("AI is switched off. Turn it on in Settings › AI.");
            return await session.Ai.Index.SearchAsync(q, workspace, Math.Clamp(limit ?? 30, 1, 100), ctx.RequestAborted);
        });

        api.MapPost("/reindex", (VaultHost host, HttpContext ctx) =>
        {
            PersonOnly(ctx);
            host.Require().Ai.Index.Refresh();
            return Results.NoContent();
        });

        api.MapPost("/ask", async (VaultHost host, HttpContext ctx, AiAskBody body) =>
        {
            var ai = host.Require().Ai;
            await Stream(ctx, async write =>
            {
                await foreach (var chunk in ai.AskAsync(body.Question, body.Workspace, ctx.Actor(), ctx.RequestAborted))
                {
                    if (chunk.Sources is not null) await write("sources", chunk.Sources);
                    if (chunk.Text is not null) await write("text", chunk.Text);
                }
            });
        });

        api.MapPost("/transform", async (VaultHost host, HttpContext ctx, AiTransformBody body) =>
        {
            var ai = host.Require().Ai;
            await Stream(ctx, async write =>
            {
                await foreach (var text in ai.TransformAsync(body.Instruction, body.Text, body.Title, ctx.Actor(), ctx.RequestAborted))
                    await write("text", text);
            });
        });
    }

    /// <summary>Server-sent events: named events with JSON data, then "done" or "error" (problems after the stream has started can't be an HTTP status).</summary>
    private static async Task Stream(HttpContext ctx, Func<Func<string, object, Task>, Task> body)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-store";
        async Task Write(string name, object data)
        {
            await ctx.Response.WriteAsync($"event: {name}\ndata: {JsonSerializer.Serialize(data, Json)}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }
        try
        {
            await body(Write);
            await Write("done", new { });
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { }
        catch (QuantosException ex) { await Write("error", new { error = ex.Message }); }
        catch (Exception ex) { await Write("error", new { error = "The AI request failed: " + ex.Message }); }
    }
}
