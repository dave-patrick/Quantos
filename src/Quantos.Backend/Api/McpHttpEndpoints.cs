using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Agents;
using Quantos.Core.Security;

namespace Quantos.Backend.Api;

public sealed record McpHttpConnectionBody(string? Name, string? Approval);

/// <summary>Settings › MCP › HTTP endpoint: where the app's Streamable HTTP MCP is, and creating a key for an agent to use it.</summary>
public static class McpHttpEndpoints
{
    public const string DefaultAgent = "http-mcp";

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/mcp-http");

        api.MapGet("", (ServerAddress address) => new
        {
            url = address.Url.TrimEnd('/') + McpHttp.Route,
            transport = "streamable-http",
            defaultAgent = DefaultAgent,
            tools = McpTools.List.Select(t => new { t.Name, t.Description })
        });

        // A connection key is an ordinary paired agent connection: listed and revoked under Paired connections,
        // and limited to its own grants. Creating one asks for the same confirmation as revoking one.
        api.MapPost("/connection", (VaultHost host, ClientCredentials credentials, ServerAddress address, HttpContext ctx, McpHttpConnectionBody body) =>
        {
            if (ctx.Actor().Kind != ActorKind.User) throw new QuantosException("Only you can create connections.", 403);
            var name = string.IsNullOrWhiteSpace(body.Name) ? DefaultAgent : body.Name.Trim();
            host.Require().Permissions.Demand(ctx.Actor(), Permission.ManagePermissions, $"Create an HTTP MCP connection for agent “{name}”", body.Approval);
            var issued = credentials.Issue(ActorKind.Agent, name);
            var url = address.Url.TrimEnd('/') + McpHttp.Route;
            return new
            {
                id = issued.Id, name, url, token = issued.Token,
                config = new { url, headers = new { Authorization = "Bearer " + issued.Token } }
            };
        });
    }
}
