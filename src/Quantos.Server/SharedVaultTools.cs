using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Quantos.Core;
using Quantos.Core.Agents;
using Quantos.Core.Security;
using Quantos.Core.Storage;

namespace Quantos.Server;

/// <summary>Remote MCP tools use the same services and file writer as both applications.</summary>
[McpServerToolType]
public sealed class SharedVaultTools(VaultSession vault)
{
    public const string AgentName = "server-mcp";
    private static readonly Actor Agent = new(ActorKind.Agent, AgentName);
    // The MCP SDK hides every exception message except McpException's, so a refusal (read-only key, note not found) would
    // reach the agent as "An error occurred invoking ..." with no reason.
    private T Run<T>(Func<T> action)
    {
        try
        {
            return vault.Writes.Exclusive(() =>
            {
                vault.Permissions.Demand(Agent, Permission.Read, "Read the shared vault");
                vault.Reconciler.Run();
                return action();
            });
        }
        catch (Exception ex) when (ex is QuantosException or PermissionDeniedException or ApprovalRequiredException)
        {
            throw new McpException(ex is PermissionDeniedException
                ? ex.Message.TrimEnd('.') + ". The server MCP is read-only unless write access is turned on in Settings › MCP › Shared server MCP."
                : ex.Message);
        }
    }
    private readonly VaultToolRunner _tools = new(vault, Agent);

    [McpServerTool(Name = "search_notes", ReadOnly = true), Description("Search the shared Quantos vault. Returns titles, ids and matching snippets.")]
    public string SearchNotes(string query, string? workspace = null) => Run(() => _tools.SearchNotes(query, workspace));

    [McpServerTool(Name = "read_note", ReadOnly = true), Description("Read a note's complete Markdown and frontmatter by id or exact title.")]
    public string ReadNote(string note) => Run(() => _tools.ReadNote(note));

    [McpServerTool(Name = "list_tasks", ReadOnly = true), Description("List tasks in the shared vault, optionally including completed tasks.")]
    public string ListTasks(string? workspace = null, bool include_done = false) => Run(() => _tools.ListTasks(workspace, include_done));

    [McpServerTool(Name = "run_query", ReadOnly = true), Description("Run a structured YAML query over the shared Quantos vault.")]
    public string RunQuery(string query) => Run(() => _tools.RunQuery(query));

    [McpServerTool(Name = "create_note"), Description("Create a Markdown note. Requires write access enabled in Quantos Settings › MCP.")]
    public string CreateNote(string title, string body = "", string workspace = "default", string[]? tags = null) => Run(() => _tools.CreateNote(title, body, workspace, tags));

    [McpServerTool(Name = "append_to_note"), Description("Append text to a note by id or exact title. Requires MCP write access.")]
    public string AppendToNote(string note, string text) => Run(() => _tools.AppendToNote(note, text));

    [McpServerTool(Name = "create_task"), Description("Create a shared task with an optional due date and priority 1–4. Requires MCP write access.")]
    public string CreateTask(string title, string? due = null, int? priority = null, string workspace = "default") => Run(() => _tools.CreateTask(title, due, priority, workspace));

    [McpServerTool(Name = "complete_task"), Description("Complete a shared task by id or exact title. Requires MCP write access.")]
    public string CompleteTask(string task) => Run(() => _tools.CompleteTask(task));

    [McpServerTool(Name = "capture"), Description("Capture text to today's daily note, an inbox note or a task. Requires MCP write access.")]
    public string Capture(string text, string to = "daily") => Run(() => _tools.Capture(text, to));
}
