using System.Text.Json.Nodes;

namespace Quantos.Core.Agents;

/// <summary>
/// The tools an agent gets over the Model Context Protocol (BUILD-PLAN 8E.13). The schemas live
/// here so the CLI can serve them and the app can show them in Settings › AI agents.
/// </summary>
public static class McpTools
{
    /// <summary>The tool schemas, in the shape MCP expects.</summary>
    public static JsonArray All { get; } =
    [
        Tool("search_notes", "Full-text search across the vault. Returns titles, ids and matching snippets.",
            new JsonObject { ["query"] = Str("Words to search for"), ["workspace"] = Str("Optional workspace name") }, "query"),
        Tool("read_note", "Read a note's Markdown (with YAML frontmatter) by id or exact title.",
            new JsonObject { ["note"] = Str("Note id or exact title") }, "note"),
        Tool("create_note", "Create a note.",
            new JsonObject { ["title"] = Str("Title"), ["body"] = Str("Markdown body"), ["workspace"] = Str("Workspace (default: default)"), ["tags"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } } }, "title"),
        Tool("append_to_note", "Append Markdown to the end of a note.",
            new JsonObject { ["note"] = Str("Note id or exact title"), ["text"] = Str("Markdown to append") }, "note", "text"),
        Tool("list_tasks", "List open tasks (or all with include_done).",
            new JsonObject { ["workspace"] = Str("Optional workspace"), ["include_done"] = new JsonObject { ["type"] = "boolean" } }),
        Tool("create_task", "Create a task.",
            new JsonObject { ["title"] = Str("Task title"), ["due"] = Str("Due date YYYY-MM-DD"), ["priority"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 4 }, ["workspace"] = Str("Workspace") }, "title"),
        Tool("complete_task", "Mark a task done.", new JsonObject { ["task"] = Str("Task id or exact title") }, "task"),
        Tool("run_query", "Run a Quantos query (YAML: type, tags, where, sort, columns).", new JsonObject { ["query"] = Str("YAML query text") }, "query"),
        Tool("capture", "Quick capture text to today's daily note, a new note (inbox) or a task.",
            new JsonObject { ["text"] = Str("Text"), ["to"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("daily", "inbox", "task") } }, "text")
    ];

    /// <summary>Name and description of each tool, for showing in the app.</summary>
    public static IReadOnlyList<(string Name, string Description)> List =>
        All.Select(t => ((string)t!["name"]!, (string)t!["description"]!)).ToList();

    private static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

    private static JsonObject Tool(string name, string description, JsonObject properties, params string[] required) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()) }
    };
}
