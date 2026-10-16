using System.Text.Json;
using System.Text.RegularExpressions;

namespace Quantos.Core.Extensibility;

public sealed record PluginCommand(string Id, string Title);

public sealed record PluginManifest(string Name, string Version, string? Description, string Main, IReadOnlyList<PluginCommand> Commands, IReadOnlyList<string> Permissions, string? Error);

/// <summary>
/// Plugins live in Vault/plugins/&lt;name&gt;/plugin.json + main.js. Their code runs in a sandboxed
/// frame with no access to the app page, cookies or native functions; every request they make
/// is checked against the grants for that plugin (stored outside the vault).
/// </summary>
public static partial class PluginCatalog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<PluginManifest> List(Storage.Vault vault)
    {
        if (!Directory.Exists(vault.Plugins)) return [];
        var list = new List<PluginManifest>();
        foreach (var dir in Directory.EnumerateDirectories(vault.Plugins).Order())
        {
            var name = Path.GetFileName(dir);
            if (!SafeName().IsMatch(name)) continue;
            var manifestPath = Path.Combine(dir, "plugin.json");
            try
            {
                using var doc = JsonDocument.Parse(Storage.AtomicFile.ReadAllText(manifestPath));
                var root = doc.RootElement;
                string? Str(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var commands = root.TryGetProperty("commands", out var c) && c.ValueKind == JsonValueKind.Array
                    ? c.EnumerateArray().Select(x => new PluginCommand(x.GetProperty("id").GetString()!, x.GetProperty("title").GetString()!)).ToList() : [];
                var perms = root.TryGetProperty("permissions", out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
                var main = Str("main") ?? "main.js";
                var error = SafeFile().IsMatch(main) && File.Exists(Path.Combine(dir, main)) ? null : $"{main} is missing.";
                list.Add(new PluginManifest(name, Str("version") ?? "0.0.0", Str("description"), main, commands, perms, error));
            }
            catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                list.Add(new PluginManifest(name, "?", null, "main.js", [], [], "plugin.json couldn't be read: " + ex.Message));
            }
        }
        return list;
    }

    public static string? Script(Storage.Vault vault, string name)
    {
        var manifest = List(vault).FirstOrDefault(p => p.Name == name);
        if (manifest is null || manifest.Error is not null) return null;
        return Storage.AtomicFile.ReadAllText(Path.Combine(vault.Plugins, name, manifest.Main));
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    private static partial Regex SafeName();

    [GeneratedRegex(@"^[\w\-]+\.js$")]
    private static partial Regex SafeFile();
}
