using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Quantos.Cli;

/// <summary>
/// The Quantos command line (BUILD-PLAN 8E.12): readable tables by default, --json for scripts.
/// It talks to the running backend through the discovery file, as the local user or, with
/// --agent, as a named agent limited to that agent's grants.
/// </summary>
public static class Program
{
    private const string Usage = """
        quantos-cli: work with your Quantos vault from the command line

        Usage: quantos-cli <command> [arguments] [--json] [--workspace <name>] [--agent <name>]

          status                          Backend, vault and index status
          search <words>                  Full-text search
          list [--type T] [--tag T]       List notes (newest first)
          show <title or id>              Print a note's Markdown
          new <title> [--body text|-]     Create a note (use - to read the body from stdin)
          append <title or id> <text>     Append text to a note
          capture <text> [--to daily|inbox|task]
                                          Quick capture (default: today's daily note)
          daily [YYYY-MM-DD]              Print a daily note
          tasks [--all]                   Open tasks (or all)
          task add <title> [--due D] [--priority 1-4]
          task done <title or id>
          query "<yaml>"                  Run a query (same syntax as ```query blocks)
          open [title or id]              Open Quantos in your browser
          run "<powershell>"              Run a command after approval in the Windows client
          rebuild                         Rebuild the cache from the vault
          mcp                             Serve the Model Context Protocol on stdin/stdout for AI agents
                                          (use with --agent <name>; grants are set in Settings)
          tools [--format F]              Print the agent tools as openai, anthropic, json or mcp schemas
          call <tool> [<json> | -]        Run one agent tool with JSON arguments (- reads them from stdin)
                                          and print its result; exit code 1 when the tool fails

        The backend must be running (start Quantos, or Quantos.Backend.exe).
        """;

    public static async Task<int> Main(string[] args)
    {
        // UTF-8 without a byte-order mark: a BOM at the start of stdout breaks MCP clients and JSON parsers.
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var opts = Options.Parse(args);
        if (opts.Positional.Count == 0 || opts.Positional[0] is "help" or "--help" or "-h") { Console.WriteLine(Usage); return 0; }

        // Tool schemas need no backend: an agent framework can read them while Quantos is closed.
        if (opts.Positional[0] == "tools")
        {
            try { Console.WriteLine(Mcp.Export(opts.Get("format") ?? "openai")); return 0; }
            catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }
        }

        Client client;
        try { client = await Client.ConnectAsync(opts); }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 3; }

        try
        {
            return opts.Positional[0] switch
            {
                "status" => await Status(client, opts),
                "search" => await Search(client, opts),
                "list" => await List(client, opts),
                "show" => await Show(client, opts),
                "new" => await New(client, opts),
                "append" => await Append(client, opts),
                "capture" => await Capture(client, opts),
                "daily" => await Daily(client, opts),
                "tasks" => await Tasks(client, opts),
                "task" => await Task(client, opts),
                "query" => await Query(client, opts),
                "open" => await Open(client, opts),
                "run" => await Run(client, opts),
                "rebuild" => await Rebuild(client, opts),
                "mcp" => await Mcp.Serve(client, opts),
                "call" => await CallTool(client, opts),
                _ => Fail($"Unknown command '{opts.Positional[0]}'. Run quantos-cli help.")
            };
        }
        catch (ApiException ex) { Console.Error.WriteLine(ex.Message); return ex.Status == 403 ? 4 : 1; }
    }

    private static int Fail(string message) { Console.Error.WriteLine(message); return 2; }

    /// <summary>One agent tool as a command: quantos-cli call search_notes '{"query":"budget"}' (Release 1.2 adapters).</summary>
    private static async Task<int> CallTool(Client c, Options o)
    {
        var name = Arg(o, 1, "tool name");
        var raw = o.Positional.Count > 2 ? o.Positional[2] : "{}";
        if (raw == "-") raw = await Console.In.ReadToEndAsync();
        JsonNode? args;
        try { args = JsonNode.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw); }
        catch (System.Text.Json.JsonException ex) { return Fail("The arguments aren't valid JSON: " + ex.Message); }
        if (args is not JsonObject) return Fail("Arguments are a JSON object, like {\"query\": \"budget\"}.");
        var (text, isError) = await Mcp.Invoke(c, o, name, args);
        (isError ? Console.Error : Console.Out).WriteLine(text);
        return isError ? 1 : 0;
    }

    private static string Arg(Options o, int index, string name) =>
        o.Positional.Count > index ? o.Positional[index] : throw new ApiException(2, $"Missing {name}.");

    // ---- commands -------------------------------------------------------------------------------

    private static async Task<int> Status(Client c, Options o)
    {
        var s = await c.Get("status");
        if (o.Json) return Print(s);
        Console.WriteLine($"Backend   {s!["version"]} on {c.Url} (process {s["pid"]})");
        Console.WriteLine($"Vault     {s["vault"]}");
        Console.WriteLine($"Notes     {s["nodeCount"]}");
        Console.WriteLine($"Workspaces {string.Join(", ", s["workspaces"]!.AsArray().Select(w => w!.ToString()))}");
        if (s["syncedFolderWarning"] is { } w && w.ToString().Length > 0) Console.WriteLine("Warning   " + w);
        return 0;
    }

    private static async Task<int> Search(Client c, Options o)
    {
        var q = string.Join(' ', o.Positional.Skip(1));
        var hits = (await c.Get($"search?q={Uri.EscapeDataString(q)}{o.WorkspaceQuery("&")}"))!.AsArray();
        if (o.Json) return Print(hits);
        Table(hits.Select(h => new[] { h!["title"]!.ToString(), h["type"]!.ToString(), h["workspace"]!.ToString(), Snip(h["snippet"]!.ToString()) }), "Title", "Type", "Workspace", "Match");
        return 0;
    }

    private static async Task<int> List(Client c, Options o)
    {
        var path = $"nodes?{(o.Workspace is null ? "" : "workspace=" + o.Workspace + "&")}{(o.Get("type") is { } t ? "type=" + t : "")}";
        var nodes = (await c.Get(path))!.AsArray().Where(n => o.Get("tag") is not { } tag || n!["tags"]!.AsArray().Any(x => x!.ToString().Equals(tag, StringComparison.OrdinalIgnoreCase)));
        if (o.Json) return Print(new JsonArray(nodes.Select(n => n!.DeepClone()).ToArray()));
        Table(nodes.Take(o.Get("limit") is { } l ? int.Parse(l) : 100).Select(n => new[] { n!["title"]!.ToString(), n["type"]!.ToString(), n["workspace"]!.ToString(), n["modified"]?.ToString()[..Math.Min(16, n["modified"]?.ToString().Length ?? 0)] ?? "", n["id"]!.ToString() }),
            "Title", "Type", "Workspace", "Modified", "Id");
        return 0;
    }

    /// <summary>Titles that start with or contain the words typed, offered when nothing matches exactly.</summary>
    private static async Task<string> Suggest(Client c, string typed)
    {
        try
        {
            var all = (await c.Get("nodes"))!.AsArray().Select(n => n!["title"]!.ToString()).Distinct().ToList();
            var close = all.Where(t => t.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                .Concat(all.Where(t => t.Contains(typed, StringComparison.OrdinalIgnoreCase))).Distinct().Take(5).ToList();
            return close.Count == 0 ? "" : " Did you mean: " + string.Join(", ", close.Select(t => $"“{t}”")) + "?";
        }
        catch (ApiException) { return ""; }
    }

    public static async Task<string> Resolve(Client c, string titleOrId, string? workspace)
    {
        if (titleOrId.Length == 26 && titleOrId.All(char.IsLetterOrDigit)) return titleOrId;
        var hits = (await c.Get($"resolve?title={Uri.EscapeDataString(titleOrId)}{(workspace is null ? "" : "&workspace=" + Uri.EscapeDataString(workspace))}"))!.AsArray();
        if (hits.Count == 0 && workspace is not null) hits = (await c.Get($"resolve?title={Uri.EscapeDataString(titleOrId)}"))!.AsArray();
        return hits.Count switch
        {
            1 => hits[0]!["id"]!.ToString(),
            0 => throw new ApiException(404, $"No note called “{titleOrId}”." + await Suggest(c, titleOrId)),
            _ => throw new ApiException(409, $"More than one note is called “{titleOrId}”: " +
                string.Join(", ", hits.Select(h => $"{h!["id"]} ({h["workspace"]})")) + ". Use the id.")
        };
    }

    private static async Task<int> Show(Client c, Options o)
    {
        var id = await Resolve(c, string.Join(' ', o.Positional.Skip(1)), o.Workspace);
        var node = await c.Get($"nodes/{id}");
        if (o.Json) return Print(node);
        Console.Write(node!["content"]!.ToString());
        return 0;
    }

    private static async Task<int> New(Client c, Options o)
    {
        var body = o.Get("body") == "-" ? await Console.In.ReadToEndAsync() : o.Get("body") ?? "";
        var tags = o.All("tag");
        var node = await c.Post("nodes", new { title = string.Join(' ', o.Positional.Skip(1)), body, workspace = o.Workspace ?? "default", tags, type = o.Get("type") ?? "note" });
        if (o.Json) return Print(node);
        Console.WriteLine($"Created {node!["title"]} ({node["id"]}) at {node["path"]}");
        return 0;
    }

    private static async Task<int> Append(Client c, Options o)
    {
        var id = await Resolve(c, Arg(o, 1, "note"), o.Workspace);
        var text = o.Positional.Count > 2 ? string.Join(' ', o.Positional.Skip(2)) : await Console.In.ReadToEndAsync();
        var node = await c.Get($"nodes/{id}");
        var body = node!["body"]!.ToString().TrimEnd() + "\n\n" + text.Trim() + "\n";
        var result = await c.Patch($"nodes/{id}", new { baseHash = node["hash"]!.ToString(), body });
        if (o.Json) return Print(result);
        Console.WriteLine($"Appended to {node["title"]}.");
        return 0;
    }

    private static async Task<int> Capture(Client c, Options o)
    {
        var node = await c.Post("capture", new { text = string.Join(' ', o.Positional.Skip(1)), workspace = o.Workspace, target = o.Get("to") ?? "daily" });
        if (o.Json) return Print(node);
        Console.WriteLine($"Captured to {node!["title"]}.");
        return 0;
    }

    private static async Task<int> Daily(Client c, Options o)
    {
        var date = o.Positional.Count > 1 ? o.Positional[1] : DateTime.Now.ToString("yyyy-MM-dd");
        try
        {
            var node = await c.Get($"daily/{Uri.EscapeDataString(o.Workspace ?? "default")}/{date}");
            if (o.Json) return Print(node);
            Console.Write(node!["body"]!.ToString());
        }
        catch (ApiException ex) when (ex.Status == 404) { Console.WriteLine($"No daily note for {date}."); }
        return 0;
    }

    private static async Task<int> Tasks(Client c, Options o)
    {
        var all = o.Flag("all");
        var tasks = (await c.Get($"tasks?include={(all ? "all" : "open")}{o.WorkspaceQuery("&")}"))!.AsArray()
            .Where(t => all || t!["status"]!.ToString() is not ("done" or "cancelled"))
            .OrderBy(t => t!["priority"]?.GetValue<int?>() ?? 9).ThenBy(t => t!["due"]?.ToString() ?? "9999");
        if (o.Json) return Print(new JsonArray(tasks.Select(t => t!.DeepClone()).ToArray()));
        Table(tasks.Select(t => new[]
        {
            t!["status"]!.ToString() == "done" ? "[x]" : "[ ]", t["priority"] is { } p ? "P" + p : "", t["title"]!.ToString(),
            t["due"]?.ToString() ?? "", t["planned"]?.ToString() ?? "", t["id"]!.ToString()
        }), "", "Pri", "Task", "Due", "Planned", "Id");
        return 0;
    }

    private static async Task<int> Task(Client c, Options o)
    {
        switch (Arg(o, 1, "task action (add or done)"))
        {
            case "add":
                var node = await c.Post("tasks", new
                {
                    title = string.Join(' ', o.Positional.Skip(2)), workspace = o.Workspace ?? "default", due = o.Get("due"),
                    planned = o.Get("planned"), priority = o.Get("priority") is { } p ? int.Parse(p) : (int?)null
                });
                if (o.Json) return Print(node);
                Console.WriteLine($"Added task {node!["title"]} ({node["id"]}).");
                return 0;
            case "done":
                var id = await Resolve(c, string.Join(' ', o.Positional.Skip(2)), o.Workspace);
                var t = await c.Post($"tasks/{id}/status", new { status = "done" });
                if (o.Json) return Print(t);
                Console.WriteLine($"Completed {t!["title"]}.");
                return 0;
            default:
                return Fail("Use: task add <title> | task done <title or id>");
        }
    }

    private static async Task<int> Query(Client c, Options o)
    {
        var result = await c.Post("query/text", new { text = string.Join(' ', o.Positional.Skip(1)).Replace("\\n", "\n"), workspace = o.Workspace });
        if (o.Json) return Print(result);
        if (result!["error"] is { } err && err.ToString().Length > 0) return Fail(err.ToString());
        var cols = result["columns"]!.AsArray().Select(x => x!.ToString()).ToArray();
        Table(result["rows"]!.AsArray().Select(r => cols.Select(col => col switch
        {
            "title" or "type" or "workspace" or "created" or "modified" or "path" => r![col]?.ToString() ?? "",
            _ => r!["properties"]![col]?.ToString() ?? ""
        }).ToArray()), cols);
        return 0;
    }

    private static async Task<int> Open(Client c, Options o)
    {
        string? to = null;
        if (o.Positional.Count > 1) to = "/note/" + await Resolve(c, string.Join(' ', o.Positional.Skip(1)), o.Workspace);
        var r = await c.Post("auth/code", new { kind = "browser", to });
        Process.Start(new ProcessStartInfo(r!["url"]!.ToString()) { UseShellExecute = true });
        return 0;
    }

    private static async Task<int> Run(Client c, Options o)
    {
        var command = string.Join(' ', o.Positional.Skip(1));
        JsonNode? job;
        try { job = await c.Post("shell/run", new { command, cwd = o.Get("cwd") }); }
        catch (ApiException ex) when (ex.Status == 428 && ex.Body?["approval"]?["id"] is { } approval)
        {
            Console.Error.WriteLine("Waiting for approval in the Quantos Windows client…");
            var id = approval.ToString();
            while (true)
            {
                await System.Threading.Tasks.Task.Delay(1000);
                var state = (await c.Get($"approvals/{id}"))!["state"]!.ToString();
                if (state == "Approved") break;
                if (state is "Denied" or "Expired") return Fail($"The command was {state.ToLowerInvariant()}.");
            }
            job = await c.Post("shell/run", new { command, cwd = o.Get("cwd"), approval = id });
        }
        var jobId = job!["id"]!.ToString();
        while (true)
        {
            var current = await c.Get($"shell/jobs/{jobId}");
            if (current!["status"]!.ToString() == "done")
            {
                if (o.Json) return Print(current);
                Console.Write(current["output"]?.ToString());
                return current["failed"]!.GetValue<bool>() ? 1 : 0;
            }
            await System.Threading.Tasks.Task.Delay(500);
        }
    }

    private static async Task<int> Rebuild(Client c, Options o)
    {
        var r = await c.Post("system/rebuild", new { });
        if (o.Json) return Print(r);
        Console.WriteLine($"Rebuilt the cache from {r!["scanned"]} files.");
        return 0;
    }

    // ---- output ---------------------------------------------------------------------------------

    private static int Print(JsonNode? node)
    {
        Console.WriteLine(node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null");
        return 0;
    }

    private static string Snip(string s) => s.Replace("[[[", "").Replace("]]]", "").Replace('\n', ' ');

    private static void Table(IEnumerable<string[]> rows, params string[] headers)
    {
        var list = rows.ToList();
        if (list.Count == 0) { Console.WriteLine("Nothing found."); return; }
        var max = Math.Max(60, Console.IsOutputRedirected ? 200 : Console.WindowWidth);
        var widths = headers.Select((h, i) => Math.Min(48, Math.Max(h.Length, list.Max(r => r.Length > i ? r[i].Length : 0)))).ToArray();
        string Fit(string s, int w) => s.Length > w ? s[..(w - 1)] + "…" : s.PadRight(w);
        Console.WriteLine(string.Join("  ", headers.Select((h, i) => Fit(h, widths[i]))).TrimEnd());
        Console.WriteLine(string.Join("  ", widths.Select(w => new string('─', w))));
        foreach (var r in list) Console.WriteLine(string.Join("  ", r.Select((v, i) => Fit(v, widths[i]))).TrimEnd());
    }
}

public sealed class ApiException(int status, string message, JsonNode? body = null) : Exception(message)
{
    public int Status { get; } = status;
    public JsonNode? Body { get; } = body;
}

public sealed class Options
{
    public List<string> Positional { get; } = [];
    private readonly Dictionary<string, List<string>> _named = new();
    public bool Json => Flag("json");
    public string? Workspace => Get("workspace");
    public string? Agent => Get("agent");

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                var name = args[i][2..];
                var flag = name is "json" or "all";
                var value = !flag && i + 1 < args.Length ? args[++i] : "true";
                if (!o._named.TryGetValue(name, out var list)) o._named[name] = list = [];
                list.Add(value);
            }
            else o.Positional.Add(args[i]);
        }
        return o;
    }

    public string? Get(string name) => _named.TryGetValue(name, out var v) ? v[^1] : null;
    public List<string> All(string name) => _named.TryGetValue(name, out var v) ? v : [];
    public bool Flag(string name) => _named.ContainsKey(name);
    public string WorkspaceQuery(string sep) => Workspace is null ? "" : $"{sep}workspace={Uri.EscapeDataString(Workspace)}";
}

public sealed class Client
{
    private readonly HttpClient _http;
    public string Url { get; }

    private Client(string url, string token, string? agent)
    {
        Url = url;
        _http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Add("X-Quantos-Client", "cli");
        if (agent is not null) _http.DefaultRequestHeaders.Add("X-Quantos-Agent", agent);
    }

    public static async Task<Client> ConnectAsync(Options o)
    {
        var clientData = o.Get("client-data") ?? Environment.GetEnvironmentVariable("QUANTOS_CLIENT_DATA");
        if (clientData is null)
        {
            var appDir = new DirectoryInfo(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory.TrimEnd('\\', '/'));
            clientData = string.Equals(appDir.Name, "application", StringComparison.OrdinalIgnoreCase) && appDir.Parent is not null
                ? Path.Combine(appDir.Parent.FullName, "client-data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quantos", "client-data");
        }
        var file = Path.Combine(clientData, "settings", "backend.json");
        if (!File.Exists(file)) throw new InvalidOperationException("Quantos isn't running. Start Quantos (or Quantos.Backend.exe) first.");
        var info = JsonNode.Parse(await File.ReadAllTextAsync(file))!;
        var client = new Client(info["url"]!.ToString(), info["token"]!.ToString(), o.Agent);
        try { await client.Get("ping"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new InvalidOperationException("Quantos isn't responding. Start it again."); }
        return client;
    }

    public Task<JsonNode?> Get(string path) => Send(HttpMethod.Get, path, null);
    public Task<JsonNode?> Post(string path, object body) => Send(HttpMethod.Post, path, body);
    public Task<JsonNode?> Patch(string path, object body) => Send(HttpMethod.Patch, path, body);

    private async Task<JsonNode?> Send(HttpMethod method, string path, object? body)
    {
        using var req = new HttpRequestMessage(method, "api/" + path);
        if (body is not null) req.Content = JsonContent.Create(body);
        using var res = await _http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        JsonNode? json = null;
        try { json = text.Length > 0 ? JsonNode.Parse(text) : null; } catch (JsonException) { }
        if (!res.IsSuccessStatusCode)
            throw new ApiException((int)res.StatusCode, json?["error"]?.ToString() ?? $"{(int)res.StatusCode} {res.ReasonPhrase}", json);
        return json;
    }
}
