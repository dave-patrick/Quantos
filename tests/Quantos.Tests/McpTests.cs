using System.Text.Json.Nodes;
using Quantos.Core;
using Quantos.Core.Agents;

namespace Quantos.Tests;

/// <summary>The Model Context Protocol server: the tools it offers and the settings an agent needs.</summary>
public class McpTests
{
    [Fact]
    public void The_agent_tools_are_the_ones_the_guide_lists()
    {
        var names = McpTools.List.Select(t => t.Name).ToList();
        Assert.Equal(["search_notes", "read_note", "create_note", "append_to_note", "list_tasks", "create_task", "complete_task", "run_query", "capture"], names);
        Assert.All(McpTools.List, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
    }

    [Fact]
    public void The_program_the_person_chose_is_remembered()
    {
        var paths = new QuantosPaths(Path.Combine(Path.GetTempPath(), "quantos-tests", Guid.NewGuid().ToString("N")));
        paths.EnsureCreated();
        var mcp = new McpService(paths);
        var program = Path.Combine(paths.ClientData, "quantos-cli.exe");
        File.WriteAllText(program, "not really a program");

        mcp.Save(new McpSettings { Program = program });

        Assert.Equal(program, mcp.Program());
        Assert.Equal(program, mcp.Load().Program);
    }

    [Fact]
    public void A_program_that_is_not_there_is_reported_as_missing()
    {
        var paths = new QuantosPaths(Path.Combine(Path.GetTempPath(), "quantos-tests", Guid.NewGuid().ToString("N")));
        var mcp = new McpService(paths);
        mcp.Save(new McpSettings { Program = Path.Combine(paths.ClientData, "missing.exe") });
        Assert.Null(mcp.Program());
    }

    [Fact]
    public void The_configuration_names_the_program_and_the_data_folder()
    {
        var paths = new QuantosPaths(Path.Combine(Path.GetTempPath(), "quantos-tests", Guid.NewGuid().ToString("N")));
        paths.EnsureCreated();
        var mcp = new McpService(paths);
        var program = Path.Combine(paths.ClientData, "quantos-cli.exe");
        File.WriteAllText(program, "");
        mcp.Save(new McpSettings { Program = program });

        var (exe, args) = mcp.Command("tester");
        Assert.Equal(program, exe);
        Assert.Equal(["mcp", "--agent", "tester", "--client-data", paths.ClientData], args);

        // Claude Desktop and Cursor take the program and the arguments apart.
        var desktop = (JsonObject)mcp.ConfigFor("claude-desktop", "tester");
        var server = (JsonObject)desktop["mcpServers"]!["quantos"]!;
        Assert.Equal(program, server["command"]!.GetValue<string>());
        Assert.Equal("tester", server["args"]![2]!.GetValue<string>());

        // OpenCode takes the whole command in one list, program first.
        var opencode = (JsonObject)mcp.ConfigFor("opencode", "tester");
        var command = opencode["mcp"]!["quantos"]!["command"]!.AsArray();
        Assert.Equal(program, command[0]!.GetValue<string>());
        Assert.Equal("mcp", command[1]!.GetValue<string>());
    }
}
