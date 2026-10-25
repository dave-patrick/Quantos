using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Storage;

namespace Quantos.Server;

/// <summary>Remote MCP tools use the same services and file writer as both applications.</summary>
[McpServerToolType]
public sealed class SharedVaultTools(VaultSession vault)
{
    public const string AgentName = "server-mcp";
    private static readonly Actor Agent = new(ActorKind.Agent, AgentName);
    private T Run<T>(Func<T> action) => vault.Writes.Exclusive(() =>
    {
        vault.Permissions.Demand(Agent, Permission.Read, "Read the shared vault");
        vault.Reconciler.Run();
        return action();
    });
    private string Resolve(string name)
    {
        if (Ulid.IsValid(name) && vault.Writes.Get(name) is not null) return name;
        var matches = vault.Cache.FindByTitle(name, null);
        return matches.Count == 1 ? matches[0].Id : throw new QuantosException(matches.Count == 0 ? "Note not found." : "Several notes have this title; use a note id.");
    }
    private static string Json(object? value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [McpServerTool(Name = "search_notes", ReadOnly = true), Description("Search the shared Quantos vault. Returns titles, ids and matching snippets.")]
    public string SearchNotes(string query, string? workspace = null) => Run(() => Json(vault.Cache.Search(query, workspace)).Replace("[[[", "").Replace("]]]", ""));

    [McpServerTool(Name = "read_note", ReadOnly = true), Description("Read a note's complete Markdown and frontmatter by id or exact title.")]
    public string ReadNote(string note) => Run(() => vault.Writes.Require(Resolve(note)).Content);

    [McpServerTool(Name = "list_tasks", ReadOnly = true), Description("List tasks in the shared vault, optionally including completed tasks.")]
    public string ListTasks(string? workspace = null, bool include_done = false) => Run(() => Json(vault.Tasks.List(workspace, includeClosed: include_done)));

    [McpServerTool(Name = "run_query", ReadOnly = true), Description("Run a structured YAML query over the shared Quantos vault.")]
    public string RunQuery(string query) => Run(() => Json(vault.Queries.RunText(query, "default")));

    [McpServerTool(Name = "create_note"), Description("Create a Markdown note. Requires write access enabled in Quantos Settings › MCP.")]
    public string CreateNote(string title, string body = "", string workspace = "default", string[]? tags = null) => Run(() =>
        Json(vault.Writes.Create(new() { Title = title, Body = body, Workspace = workspace, Tags = tags }, Agent)));

    [McpServerTool(Name = "append_to_note"), Description("Append text to a note by id or exact title. Requires MCP write access.")]
    public string AppendToNote(string note, string text) => Run(() => Json(vault.Writes.AppendToBody(Resolve(note), text, Agent)));

    [McpServerTool(Name = "create_task"), Description("Create a shared task with an optional due date and priority 1–4. Requires MCP write access.")]
    public string CreateTask(string title, string? due = null, int? priority = null, string workspace = "default") => Run(() =>
        Json(vault.Tasks.Create(new() { Title = title, Due = due, Priority = priority, Workspace = workspace }, Agent)));

    [McpServerTool(Name = "complete_task"), Description("Complete a shared task by id or exact title. Requires MCP write access.")]
    public string CompleteTask(string task) => Run(() => Json(vault.Tasks.SetStatus(Resolve(task), "done", Agent)));

    [McpServerTool(Name = "capture"), Description("Capture text to today's daily note, an inbox note or a task. Requires MCP write access.")]
    public string Capture(string text, string to = "daily") => Run(() => Json(vault.Capture.Capture(text, "default",
        Enum.TryParse<CaptureTarget>(to, true, out var target) ? target : throw new QuantosException("Use daily, inbox or task."), Agent)));
}
