using System.Text.Json;
using System.Text.Json.Nodes;
using Quantos.Core.Security;
using Quantos.Core.Storage;

namespace Quantos.Core;

/// <summary>
/// App configuration kept in the vault (BUILD-PLAN section 4): settings.json, views.json and the
/// other root files, plus workspace-state.json per workspace. Writes are atomic and permission
/// checked; changes are announced so other clients reload.
/// </summary>
public sealed class ConfigStore(Vault vault, PermissionService permissions, ChangeHub changes)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _gate = new();

    public static bool IsAllowed(string name) => Vault.ConfigFiles.Contains(name, StringComparer.OrdinalIgnoreCase);

    public JsonNode? Read(string name)
    {
        if (!IsAllowed(name)) throw new QuantosException($"'{name}' isn't a Quantos configuration file.", 404);
        return ReadPath(Path.Combine(vault.Root, name));
    }

    public void Write(string name, JsonNode? value, Actor actor)
    {
        if (!IsAllowed(name)) throw new QuantosException($"'{name}' isn't a Quantos configuration file.", 404);
        permissions.Demand(actor, Permission.CreateEdit, $"Change {name}");
        WritePath(Path.Combine(vault.Root, name), value);
        changes.Publish(new ChangeEvent(ChangeKind.Notification, null, name, Origin: actor.ToString(), Data: new { config = name }));
    }

    public JsonNode? ReadWorkspaceState(string workspace) =>
        ReadPath(vault.WorkspaceStatePath(workspace));

    public void WriteWorkspaceState(string workspace, JsonNode? value, Actor actor)
    {
        permissions.Demand(actor, Permission.CreateEdit, $"Save layout for {workspace}");
        vault.EnsureWorkspace(workspace);
        WritePath(vault.WorkspaceStatePath(workspace), value);
    }

    public T Get<T>(string name, T fallback) where T : class
    {
        try { return Read(name)?.Deserialize<T>(Json) ?? fallback; }
        catch (JsonException) { return fallback; }
    }

    private JsonNode? ReadPath(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonNode.Parse(AtomicFile.ReadAllText(path)); }
        catch (JsonException ex) { throw new QuantosException($"{Path.GetFileName(path)} isn't valid JSON: {ex.Message}", 422); }
    }

    private void WritePath(string path, JsonNode? value)
    {
        lock (_gate) AtomicFile.WriteAllText(path, (value ?? new JsonObject()).ToJsonString(Json));
    }
}
