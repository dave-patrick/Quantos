using System.Text.Json;
using System.Text.Json.Nodes;
using Quantos.Core.Security;

namespace Quantos.Core.Agents;

/// <summary>
/// Runs the agent tools (see <see cref="McpTools"/>) directly against an open vault, as one named agent.
/// The shared server's MCP and the app's own HTTP MCP endpoint both use it, so the two behave the same.
/// Every call goes through the same services as the apps, so the agent can do only what its grants allow.
/// </summary>
public sealed class VaultToolRunner(VaultSession vault, Actor agent)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static string Json(object? value) => JsonSerializer.Serialize(value, Web);

    private string Resolve(string name)
    {
        if (Ulid.IsValid(name) && vault.Writes.Get(name) is not null) return name;
        var matches = vault.Cache.FindByTitle(name, null);
        return matches.Count == 1 ? matches[0].Id : throw new QuantosException(matches.Count == 0 ? "Note not found." : "Several notes have this title; use a note id.");
    }

    public string SearchNotes(string query, string? workspace = null) => Json(vault.Cache.Search(query, workspace)).Replace("[[[", "").Replace("]]]", "");
    public string ReadNote(string note) => vault.Writes.Require(Resolve(note)).Content;
    public string ListTasks(string? workspace = null, bool includeDone = false) => Json(vault.Tasks.List(workspace, includeClosed: includeDone));
    public string RunQuery(string query) => Json(vault.Queries.RunText(query, "default"));
    public string CreateNote(string title, string body = "", string workspace = "default", string[]? tags = null) =>
        Json(vault.Writes.Create(new() { Title = title, Body = body, Workspace = workspace, Tags = tags }, agent));
    public string AppendToNote(string note, string text) => Json(vault.Writes.AppendToBody(Resolve(note), text, agent));
    public string CreateTask(string title, string? due = null, int? priority = null, string workspace = "default") =>
        Json(vault.Tasks.Create(new() { Title = title, Due = due, Priority = priority, Workspace = workspace }, agent));
    public string CompleteTask(string task) => Json(vault.Tasks.SetStatus(Resolve(task), "done", agent));
    public string Capture(string text, string to = "daily") => Json(vault.Capture.Capture(text, "default",
        Enum.TryParse<CaptureTarget>(to, true, out var target) ? target : throw new QuantosException("Use daily, inbox or task."), agent));

    /// <summary>Runs a tool by name with MCP-style arguments. Throws <see cref="QuantosException"/> for a bad call.</summary>
    public string Call(string name, JsonNode? arguments)
    {
        var a = arguments as JsonObject ?? new JsonObject();
        string? Opt(string key) => a[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : a[key]?.ToString();
        string Req(string key) => Opt(key) is { Length: > 0 } s ? s : throw new QuantosException($"'{key}' is required.");
        if (!McpTools.All.Any(t => t!["name"]!.ToString() == name)) throw new QuantosException($"Unknown tool: {name}");
        vault.Permissions.Demand(agent, Permission.Read, "Read the vault");
        return name switch
        {
            "search_notes" => SearchNotes(Req("query"), Opt("workspace")),
            "read_note" => ReadNote(Req("note")),
            "list_tasks" => ListTasks(Opt("workspace"), a["include_done"]?.GetValue<bool>() == true),
            "run_query" => RunQuery(Req("query")),
            "create_note" => CreateNote(Req("title"), Opt("body") ?? "", Opt("workspace") ?? "default",
                a["tags"] is JsonArray tags ? tags.Select(t => t!.ToString()).ToArray() : null),
            "append_to_note" => AppendToNote(Req("note"), Req("text")),
            "create_task" => CreateTask(Req("title"), Opt("due"), a["priority"] is JsonValue p && p.TryGetValue<int>(out var n) ? n : null, Opt("workspace") ?? "default"),
            "complete_task" => CompleteTask(Req("task")),
            "capture" => Capture(Req("text"), Opt("to") ?? "daily"),
            _ => throw new QuantosException($"Unknown tool: {name}")
        };
    }
}
