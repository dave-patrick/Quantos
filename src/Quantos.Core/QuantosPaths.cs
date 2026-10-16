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

        // The executable's own folder (single-file builds may report an extraction folder as the base directory).
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        var appDir = new DirectoryInfo(!string.IsNullOrEmpty(exeDir) && !Path.GetFileName(Environment.ProcessPath!).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase)
            ? exeDir : AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(appDir.Name, "application", StringComparison.OrdinalIgnoreCase) && appDir.Parent is not null)
            return new QuantosPaths(Path.Combine(appDir.Parent.FullName, "client-data"));

        return new QuantosPaths(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quantos", "client-data"));
    }

    /// <summary>The example vault shipped beside <c>application\</c> in the package, when it's there.</summary>
    public static string? ExampleVault
    {
        get
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrEmpty(exeDir)) return null;
            var dir = new DirectoryInfo(exeDir);
            if (!string.Equals(dir.Name, "application", StringComparison.OrdinalIgnoreCase) || dir.Parent is null) return null;
            var path = Path.Combine(dir.Parent.FullName, "Example Vault");
            return Directory.Exists(path) ? path : null;
        }
    }

    public static string DefaultVault =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Quantos", "Vault");
}
