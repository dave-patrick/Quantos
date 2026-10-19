using System.Text.Json;
using System.Text.Json.Nodes;

namespace Quantos.Core.Agents;

/// <summary>What the person chose for the agent program on this computer.</summary>
public sealed record McpSettings
{
    /// <summary>The full path of <c>quantos-cli.exe</c>. Empty means "find it next to the backend".</summary>
    public string Program { get; init; } = "";
}

/// <summary>
/// The Model Context Protocol server's settings and what an agent needs to reach it
/// (BUILD-PLAN 8E.13). The settings live in client-data: they point at files on this computer.
/// </summary>
public sealed class McpService(QuantosPaths paths)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _file = Path.Combine(paths.Settings, "mcp.json");

    public QuantosPaths Paths { get; } = paths;

    public McpSettings Load()
    {
        try
        {
            if (!File.Exists(_file)) return new McpSettings();
            return JsonSerializer.Deserialize<McpSettings>(File.ReadAllText(_file), Json) ?? new McpSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return new McpSettings(); }
    }

    public void Save(McpSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(settings, Json));
    }

    /// <summary>
    /// The program that serves the protocol: the one the person chose, or the copy that sits beside
    /// the backend (how the release is laid out). Null when it can't be found.
    /// </summary>
    public string? Program()
    {
        var chosen = Load().Program;
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            var full = Environment.ExpandEnvironmentVariables(chosen.Trim().Trim('"'));
            return File.Exists(full) ? Path.GetFullPath(full) : null;
        }
        var beside = Path.Combine(AppDirectory(), "quantos-cli.exe");
        if (File.Exists(beside)) return Path.GetFullPath(beside);
        return null;
    }

    /// <summary>The folder the running program lives in.</summary>
    public static string AppDirectory()
    {
        var exe = Environment.ProcessPath;
        var dir = !string.IsNullOrEmpty(exe) && !Path.GetFileName(exe).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(exe)
            : AppContext.BaseDirectory.TrimEnd('\\', '/');
        return dir ?? AppContext.BaseDirectory;
    }

    /// <summary>
    /// The command an agent runs, and the arguments it needs. The client-data folder is always
    /// named, so the command works wherever the app is installed.
    /// </summary>
    public (string? Program, string[] Args) Command(string agent)
    {
        var program = Program();
        var args = new List<string> { "mcp", "--agent", agent, "--client-data", Paths.ClientData };
        return (program, args.ToArray());
    }

    /// <summary>A ready-made configuration for the agents people use.</summary>
    public JsonObject ConfigFor(string client, string agent)
    {
        var (program, args) = Command(agent);
        var exe = program ?? "quantos-cli.exe";
        var list = new JsonArray(args.Select(a => (JsonNode)a!).ToArray());
        return client switch
        {
            "claude-desktop" => new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    ["quantos"] = new JsonObject { ["command"] = exe, ["args"] = list }
                }
            },
            "claude-code" => new JsonObject
            {
                ["command"] = $"claude mcp add quantos -- \"{exe}\" {string.Join(" ", args)}"
            },
            "cursor" => new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    ["quantos"] = new JsonObject { ["command"] = exe, ["args"] = list }
                }
            },
            "codex" => new JsonObject
            {
                ["mcp_servers"] = new JsonObject
                {
                    ["quantos"] = new JsonObject
                    {
                        ["command"] = exe,
                        ["args"] = list,
                        ["env"] = new JsonObject { ["QUANTOS_CLIENT_DATA"] = Paths.ClientData }
                    }
                }
            },
            _ => new JsonObject
            {
                ["mcp"] = new JsonObject
                {
                    // OpenCode takes the whole command in one list, program first.
                    ["quantos"] = new JsonObject
                    {
                        ["type"] = "local",
                        ["command"] = new JsonArray((new JsonNode?[] { exe }).Concat(args.Select(a => (JsonNode)a!)).ToArray()),
                        ["enabled"] = true
                    }
                }
            }
        };
    }
}
