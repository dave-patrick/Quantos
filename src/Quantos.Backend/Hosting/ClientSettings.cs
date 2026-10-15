using System.Text.Json;
using Quantos.Core;
using Quantos.Core.Storage;

namespace Quantos.Backend.Hosting;

/// <summary>Per-device settings in client-data (never in the vault).</summary>
public sealed class ClientSettings
{
    public string? VaultPath { get; set; }
    public int? Port { get; set; }
    public string? LastWorkspace { get; set; }
    public List<string> RecentVaults { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static ClientSettings Load(QuantosPaths paths)
    {
        try
        {
            if (File.Exists(paths.ClientSettingsFile))
                return JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(paths.ClientSettingsFile), Json) ?? new();
        }
        catch (JsonException) { }
        return new();
    }

    public void Save(QuantosPaths paths)
    {
        paths.EnsureCreated();
        AtomicFile.WriteAllText(paths.ClientSettingsFile, JsonSerializer.Serialize(this, Json));
    }
}
