using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Quantos.Core;
using Quantos.Core.Agents;
using Quantos.Core.Security;

namespace Quantos.Backend.Hosting;

/// <summary>
/// The app's own Model Context Protocol endpoint over Streamable HTTP: <c>POST /mcp</c> on the backend's loopback address.
/// It is stateless (no session id, no server-initiated stream) and answers each JSON-RPC message with one JSON response.
/// It accepts only a paired <b>agent</b> connection as <c>Authorization: Bearer</c>; the agent then has exactly the grants
/// it has for the command-line MCP (read by default). Host and Origin checks run before it, so a web page cannot reach it.
/// </summary>
public static class McpHttp
{
    public const string Route = "/mcp";
    private const int MaxBody = 1024 * 1024;
    private const string Latest = "2025-06-18";
    private static readonly string[] Versions = [Latest, "2025-03-26", "2024-11-05"];
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private const string Instructions = "Quantos is the user's personal knowledge vault of Markdown notes and tasks. Notes link with [[id|Title]]. Prefer reading before writing; keep the user's formatting.";

    public static bool Matches(PathString path) => path == Route || path == Route + "/";

    public static async Task HandleAsync(HttpContext ctx, ClientCredentials credentials)
    {
        var request = ctx.Request;
        var response = ctx.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers.XContentTypeOptions = "nosniff";

        var auth = request.Headers.Authorization.ToString();
        var actor = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? credentials.Authenticate(auth[7..].Trim()) : null;
        if (actor is null)
        {
            response.Headers.WWWAuthenticate = "Bearer realm=\"quantos\"";
            await Plain(response, StatusCodes.Status401Unauthorized, "A paired agent connection is required (Settings › MCP › HTTP endpoint).");
            return;
        }
        if (actor.Kind != ActorKind.Agent)
        {
            await Plain(response, StatusCodes.Status403Forbidden, "This endpoint takes agent connections only.");
            return;
        }
        if (!HttpMethods.IsPost(request.Method))
        {
            response.Headers.Allow = "POST";
            await Plain(response, StatusCodes.Status405MethodNotAllowed, "Send JSON-RPC messages with POST. This endpoint keeps no session and has no event stream.");
            return;
        }

        string text;
        using (var buffer = new MemoryStream())
        {
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await request.Body.ReadAsync(chunk, ctx.RequestAborted)) > 0)
            {
                if (buffer.Length + read > MaxBody) { await Plain(response, StatusCodes.Status413PayloadTooLarge, "The request is too large."); return; }
                buffer.Write(chunk, 0, read);
            }
            text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }

        JsonNode? body;
        try { body = JsonNode.Parse(text); }
        catch (JsonException) { await Write(ctx, StatusCodes.Status400BadRequest, Error(null, -32700, "Parse error")); return; }

        var host = ctx.RequestServices.GetRequiredService<VaultHost>();
        var log = ctx.RequestServices.GetRequiredService<ILogger<VaultHost>>();
        JsonNode? reply;
        if (body is JsonArray batch)
        {
            if (batch.Count == 0) { await Write(ctx, StatusCodes.Status400BadRequest, Error(null, -32600, "Empty batch")); return; }
            var replies = batch.Select(m => Process(m, host, actor, log)).Where(r => r is not null).ToArray();
            reply = replies.Length == 0 ? null : new JsonArray(replies!);
        }
        else reply = Process(body, host, actor, log);

        if (reply is null) { response.StatusCode = StatusCodes.Status202Accepted; return; }
        await Write(ctx, StatusCodes.Status200OK, reply);
    }

    /// <summary>One JSON-RPC message. Notifications and responses from the client get no reply.</summary>
    private static JsonNode? Process(JsonNode? message, VaultHost host, Actor actor, ILogger log)
    {
        if (message is not JsonObject o) return Error(null, -32600, "Invalid request");
        var method = o["method"]?.GetValueKind() == JsonValueKind.String ? o["method"]!.ToString() : null;
        var id = o["id"]?.DeepClone();
        if (method is null) return id is null || o.ContainsKey("result") || o.ContainsKey("error") ? null : Error(id, -32600, "Invalid request");
        if (id is null) return null; // a notification, such as notifications/initialized
        try
        {
            JsonNode result = method switch
            {
                "initialize" => new JsonObject
                {
                    ["protocolVersion"] = Negotiate(o["params"]?["protocolVersion"]?.ToString()),
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "quantos", ["version"] = Api.SystemEndpoints.Version },
                    ["instructions"] = Instructions
                },
                "ping" => new JsonObject(),
                "tools/list" => new JsonObject { ["tools"] = McpTools.All.DeepClone() },
                "tools/call" => CallTool(host, actor, o["params"], log),
                _ => throw new RpcError(-32601, $"Method not found: {method}")
            };
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
        }
        catch (RpcError e) { return Error(id, e.Code, e.Message); }
    }

    private static string Negotiate(string? requested) => requested is not null && Versions.Contains(requested) ? requested : Latest;

    private static JsonObject CallTool(VaultHost host, Actor actor, JsonNode? p, ILogger log)
    {
        var name = p?["name"]?.ToString();
        if (string.IsNullOrEmpty(name)) throw new RpcError(-32602, "A tool name is required.");
        if (!McpTools.All.Any(t => t!["name"]!.ToString() == name)) throw new RpcError(-32602, $"Unknown tool: {name}");
        try
        {
            var session = host.Require();
            return Content(new VaultToolRunner(session, actor).Call(name, p?["arguments"]), false);
        }
        catch (PermissionDeniedException ex)
        {
            return Content(ex.Message.TrimEnd('.') + ". The user can grant this agent more in Quantos Settings › Plugins & permissions.", true);
        }
        catch (ApprovalRequiredException ex)
        {
            return Content(ex.Message, true);
        }
        catch (QuantosException ex)
        {
            return Content(ex.Message, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "HTTP MCP tool {Tool} failed", name);
            return Content("Unexpected error: " + ex.Message, true);
        }
    }

    private static JsonObject Content(string text, bool error)
    {
        var result = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) };
        if (error) result["isError"] = true;
        return result;
    }

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    /// <summary>JSON, or a single server-sent event when the client accepts only text/event-stream.</summary>
    private static async Task Write(HttpContext ctx, int status, JsonNode body)
    {
        var json = body.ToJsonString(Compact);
        var accept = ctx.Request.Headers.Accept.ToString();
        var sse = accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) && !accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) && !accept.Contains("*/*", StringComparison.Ordinal);
        ctx.Response.StatusCode = status;
        if (sse)
        {
            ctx.Response.ContentType = "text/event-stream";
            await ctx.Response.WriteAsync($"event: message\ndata: {json}\n\n", Encoding.UTF8, ctx.RequestAborted);
        }
        else
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(json, Encoding.UTF8, ctx.RequestAborted);
        }
    }

    private static Task Plain(HttpResponse response, int status, string message)
    {
        response.StatusCode = status;
        return response.WriteAsJsonAsync(new { error = message });
    }

    private sealed class RpcError(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }
}
