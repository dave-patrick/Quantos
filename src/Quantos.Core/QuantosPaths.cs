namespace Quantos.Core;

/// <summary>Resolves the client-data folder and the default vault location (STORAGE-SPEC section 1).</summary>
public sealed class QuantosPaths
{
    public QuantosPaths(string clientData)
    {
        ClientData = Path.GetFullPath(clientData);
    }

    public string ClientData { get; }
    public string Settings => Path.Combine(ClientData, "settings");
    public string Cache => Path.Combine(ClientData, "cache");
    public string CacheDatabase => Path.Combine(Cache, "cache.sqlite");
    public string Logs => Path.Combine(ClientData, "logs");
    public string WebView2 => Path.Combine(ClientData, "webview2");
    public string DiscoveryFile => Path.Combine(Settings, "backend.json");
    public string ClientSettingsFile => Path.Combine(Settings, "client.json");
    public string PermissionsFile => Path.Combine(Settings, "permissions.json");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Settings);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Logs);
    }

    /// <summary>
    /// <c>Quantos\client-data</c> beside <c>Quantos\application</c>; falls back to
    /// <c>%LOCALAPPDATA%\Quantos\client-data</c> when the app isn't in an "application" folder.
    /// <c>QUANTOS_CLIENT_DATA</c> overrides both.
    /// </summary>
    public static QuantosPaths Resolve(string? overridePath = null)
    {
        var configured = overridePath ?? Environment.GetEnvironmentVariable("QUANTOS_CLIENT_DATA");
        if (!string.IsNullOrWhiteSpace(configured)) return new QuantosPaths(configured);

        var appDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(appDir.Name, "application", StringComparison.OrdinalIgnoreCase) && appDir.Parent is not null)
            return new QuantosPaths(Path.Combine(appDir.Parent.FullName, "client-data"));

        return new QuantosPaths(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quantos", "client-data"));
    }

    public static string DefaultVault =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Quantos", "Vault");
}
