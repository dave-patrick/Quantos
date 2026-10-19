using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantos.Core.Storage;

namespace Quantos.Core.Ai;

/// <summary>One use of AI (writing/answering, or embeddings for meaning search): which provider, where, which model.</summary>
public sealed record AiEndpoint(string Provider, string? BaseUrl, string? Model)
{
    public AiPreset? Preset => AiProviders.Find(Provider);
    public string Url => (string.IsNullOrWhiteSpace(BaseUrl) ? Preset?.BaseUrl ?? "" : BaseUrl).TrimEnd('/');
    public AiApi Api => Preset?.Api ?? AiApi.OpenAiCompatible;
    public bool IsLocal => Uri.TryCreate(Url, UriKind.Absolute, out var u) && (u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Per computer (client-data), not in the vault: a vault may be shared, keys must not be.</summary>
public sealed record AiSettings
{
    /// <summary>Master switch. Off means nothing is ever sent anywhere.</summary>
    public bool Enabled { get; init; }
    public AiEndpoint? Chat { get; init; }
    public AiEndpoint? Embeddings { get; init; }
}

/// <summary>
/// Reads and writes ai.json, and keeps API keys in ai-keys.json encrypted for the current Windows
/// user (DPAPI), so a copied client-data folder doesn't carry usable keys. Keys never go back to a page.
/// </summary>
public sealed class AiSettingsStore(string settingsFolder)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Quantos AI provider key");
    private readonly object _gate = new();
    private string SettingsFile => Path.Combine(settingsFolder, "ai.json");
    private string KeysFile => Path.Combine(settingsFolder, "ai-keys.json");

    public AiSettings Load()
    {
        lock (_gate)
        {
            try { return File.Exists(SettingsFile) ? JsonSerializer.Deserialize<AiSettings>(AtomicFile.ReadAllText(SettingsFile), Json) ?? new() : new(); }
            catch (JsonException) { return new(); }
        }
    }

    public void Save(AiSettings settings)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(settingsFolder);
            AtomicFile.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, Json));
        }
    }

    public bool HasKey(string provider) => ReadKeys().ContainsKey(provider);

    public string? GetKey(string provider)
    {
        if (!ReadKeys().TryGetValue(provider, out var sealedKey)) return null;
        if (!OperatingSystem.IsWindows()) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(sealedKey), Entropy, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException) { return null; } // sealed by another Windows user or machine
        catch (FormatException) { return null; }
    }

    public void SetKey(string provider, string? key)
    {
        if (!OperatingSystem.IsWindows()) throw new QuantosException("Saving API keys needs Windows (they're encrypted for your Windows account).");
        lock (_gate)
        {
            var keys = ReadKeys();
            if (string.IsNullOrWhiteSpace(key)) keys.Remove(provider);
            else keys[provider] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser));
            Directory.CreateDirectory(settingsFolder);
            AtomicFile.WriteAllText(KeysFile, JsonSerializer.Serialize(keys, Json));
        }
    }

    private Dictionary<string, string> ReadKeys()
    {
        lock (_gate)
        {
            try { return File.Exists(KeysFile) ? JsonSerializer.Deserialize<Dictionary<string, string>>(AtomicFile.ReadAllText(KeysFile), Json) ?? new() : new(); }
            catch (JsonException) { return new(); }
        }
    }
}
