using System.Globalization;
using System.Text.Json;
using Quantos.Backend.Hosting;
using Quantos.Core;

namespace Quantos.Backend.Api;

public sealed record HabitBody(string Key, JsonElement? Value);

/// <summary>Habit and metric tracking (Release 1.1): values live on daily notes.</summary>
public static class HabitEndpoints
{
    private static DateOnly Day(string s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        ? d : throw new QuantosException("Dates look like 2026-10-18.");

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/habits");

        api.MapGet("/{workspace}", (VaultHost host, HttpContext ctx, string workspace, string from, string to, string keys) =>
            host.Require().Habits.Read(workspace, Day(from), Day(to), keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), ctx.Actor()));

        api.MapPut("/{workspace}/{date}", (VaultHost host, HttpContext ctx, string workspace, string date, HabitBody body) =>
        {
            object? value = body.Value?.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.Number => body.Value.Value.TryGetInt64(out var l) ? l : body.Value.Value.GetDouble(),
                JsonValueKind.String => body.Value.Value.GetString(),
                _ => null
            };
            var node = host.Require().Habits.Set(workspace, Day(date), body.Key, value, ctx.Actor());
            return new { ok = true, nodeId = node?.Id };
        });
    }
}
