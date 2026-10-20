using System.Text.Json;
using System.Text.Json.Nodes;
using Quantos.Core.Agents;

namespace Quantos.Cli;

/// <summary>
/// Agent interface (BUILD-PLAN 8E.13): a Model Context Protocol server over stdin/stdout. Every
/// tool call goes through the backend as the named agent, so the agent can only do what its
/// grants allow (read by default). Shell commands always need approval in the Windows client.
/// </summary>
public static class Mcp
{
    // Relaxed escaping: this goes to an agent over stdin/stdout, never into HTML, so \u0022-style escapes only waste tokens.
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The tools the agent gets. The schemas live in Core, so the app can show them too.</summary>
    private static JsonArray Tools => McpTools.All;

    /// <summary>
    /// The same tools in the shapes other agent frameworks take (Release 1.2 adapters): "openai" for
    /// Chat Completions / Responses function tools, "anthropic" for the Messages API, "json" for a plain list
    /// of name, description and JSON Schema. Frameworks then run a tool with <c>quantos-cli call</c>.
    /// </summary>
    public static string Export(string format)
    {
        var list = Tools.Select(t => t!.AsObject()).ToList();
        JsonNode result = format switch
        {
            "openai" => new JsonArray(list.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = t["name"]!.DeepClone(), ["description"] = t["description"]!.DeepClone(), ["parameters"] = t["inputSchema"]!.DeepClone() }
            }).ToArray()),
            "anthropic" => new JsonArray(list.Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t["name"]!.DeepClone(), ["description"] = t["description"]!.DeepClone(), ["input_schema"] = t["inputSchema"]!.DeepClone()
            }).ToArray()),
            "json" => new JsonArray(list.Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t["name"]!.DeepClone(), ["description"] = t["description"]!.DeepClone(), ["parameters"] = t["inputSchema"]!.DeepClone()
            }).ToArray()),
            "mcp" => new JsonObject { ["tools"] = Tools.DeepClone() },
            _ => throw new ArgumentException($"Unknown format '{format}'. Use openai, anthropic, json or mcp.")
        };
        return result.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Runs one tool outside an MCP session (for frameworks that call tools as commands).</summary>
    public static async Task<(string Text, bool IsError)> Invoke(Client client, Options options, string name, JsonNode? arguments)
    {
        if (!Tools.Any(t => t!["name"]!.ToString() == name)) return ($"Unknown tool '{name}'. Run quantos-cli tools to list them.", true);
        var result = await Call(client, options, new JsonObject { ["name"] = name, ["arguments"] = arguments ?? new JsonObject() });
        return (result["content"]![0]!["text"]!.ToString(), result["isError"]?.GetValue<bool>() == true);
    }

    public static async Task<int> Serve(Client client, Options options)
    {
        if (options.Agent is null) Console.Error.WriteLine("Tip: pass --agent <name> so this agent gets its own permissions (read-only until you grant more).");
        string? line;
        while ((line = await Console.In.ReadLineAsync()) is not null)
        {
            if (line.Trim().Length == 0) continue;
            JsonNode? request;
            try { request = JsonNode.Parse(line); }
            catch (JsonException) { Write(Error(null, -32700, "Parse error")); continue; }
            var id = request?["id"]?.DeepClone();
            var method = request?["method"]?.ToString();
            if (id is null) continue; // notifications need no reply
            try
            {
                var result = method switch
                {
                    "initialize" => new JsonObject
                    {
                        ["protocolVersion"] = request?["params"]?["protocolVersion"]?.ToString() ?? "2025-06-18",
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = "quantos", ["version"] = "0.1.5.3" },
                        ["instructions"] = "Quantos is the user's personal knowledge vault of Markdown notes and tasks. Notes link with [[id|Title]]. Prefer reading before writing; keep the user's formatting."
                    },
                    "ping" => new JsonObject(),
                    "tools/list" => new JsonObject { ["tools"] = Tools.DeepClone() },
                    "tools/call" => await Call(client, options, request!["params"]!),
                    _ => throw new McpError(-32601, $"Method not found: {method}")
                };
                Write(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
            }
            catch (McpError e) { Write(Error(id, e.Code, e.Message)); }
        }
        return 0;
    }

    private static async Task<JsonObject> Call(Client c, Options o, JsonNode p)
    {
        var name = p["name"]?.ToString();
        var a = p["arguments"] ?? new JsonObject();
        string S(string key) => a[key]?.ToString() ?? "";
        try
        {
            string text = name switch
            {
                // Snippets mark hits with [[[ ]]] for the interface; agents would read those as wiki links.
                "search_notes" => Json(await c.Get($"search?q={Uri.EscapeDataString(S("query"))}{(a["workspace"] is null ? "" : "&workspace=" + Uri.EscapeDataString(S("workspace")))}")).Replace("[[[", "").Replace("]]]", ""),
                "read_note" => (await c.Get($"nodes/{await Program.Resolve(c, S("note"), null)}"))!["content"]!.ToString(),
                "create_note" => Json(await c.Post("nodes", new
                {
                    title = S("title"), body = S("body"), workspace = a["workspace"]?.ToString() ?? "default",
                    tags = a["tags"]?.AsArray().Select(t => t!.ToString()).ToList()
                })),
                "append_to_note" => await AppendNote(c, S("note"), S("text")),
                "list_tasks" => Json(await c.Get($"tasks?include={(a["include_done"]?.GetValue<bool>() == true ? "all" : "open")}{(a["workspace"] is null ? "" : "&workspace=" + Uri.EscapeDataString(S("workspace")))}")),
                "create_task" => Json(await c.Post("tasks", new
                {
                    title = S("title"), due = a["due"]?.ToString(), priority = a["priority"]?.GetValue<int?>(), workspace = a["workspace"]?.ToString() ?? "default"
                })),
                "complete_task" => Json(await c.Post($"tasks/{await Program.Resolve(c, S("task"), null)}/status", new { status = "done" })),
                "run_query" => Json(await c.Post("query/text", new { text = S("query") })),
                "capture" => Json(await c.Post("capture", new { text = S("text"), target = a["to"]?.ToString() ?? "daily" })),
                _ => throw new McpError(-32602, $"Unknown tool: {name}")
            };
            return new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) };
        }
        catch (ApiException ex)
        {
            var hint = ex.Status == 403 ? " (this agent hasn't been granted that; the user can grant it in Quantos Settings › Plugins & permissions)" : "";
            return new JsonObject { ["isError"] = true, ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = ex.Message + hint }) };
        }
    }

    private static async Task<string> AppendNote(Client c, string note, string text)
    {
        var id = await Program.Resolve(c, note, null);
        var node = await c.Get($"nodes/{id}");
        var body = node!["body"]!.ToString().TrimEnd() + "\n\n" + text.Trim() + "\n";
        await c.Patch($"nodes/{id}", new { baseHash = node["hash"]!.ToString(), body });
        return $"Appended to {node["title"]}.";
    }

    // Compact: tool results go into an agent's context, where indentation only costs tokens.
    private static string Json(JsonNode? n) => n?.ToJsonString(Compact) ?? "null";

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static void Write(JsonObject message)
    {
        Console.Out.WriteLine(message.ToJsonString(Compact));
        Console.Out.Flush();
    }

    private sealed class McpError(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }
}
