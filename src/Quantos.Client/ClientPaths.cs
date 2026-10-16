using System.Text.Json;

namespace Quantos.Client;

/// <summary>Same rules as the backend (STORAGE-SPEC section 1): client-data beside "application", else %LOCALAPPDATA%.</summary>
public sealed class ClientPaths
{
    public ClientPaths(string clientData) => ClientData = Path.GetFullPath(clientData);

    public string ClientData { get; }
    public string Settings => Path.Combine(ClientData, "settings");
    public string Logs => Path.Combine(ClientData, "logs");
    public string WebView2 => Path.Combine(ClientData, "webview2");
    public string DiscoveryFile => Path.Combine(Settings, "backend.json");
    public string WindowFile => Path.Combine(Settings, "client-window.json");

    public static ClientPaths Resolve(string? overridePath = null)
    {
        var configured = overridePath ?? Environment.GetEnvironmentVariable("QUANTOS_CLIENT_DATA");
        if (!string.IsNullOrWhiteSpace(configured)) return new ClientPaths(configured);
        var appDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd('\\', '/'));
        if (string.Equals(appDir.Name, "application", StringComparison.OrdinalIgnoreCase) && appDir.Parent is not null)
            return new ClientPaths(Path.Combine(appDir.Parent.FullName, "client-data"));
        return new ClientPaths(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quantos", "client-data"));
    }

    public void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Logs);
            File.AppendAllText(Path.Combine(Logs, $"client-{DateTime.Now:yyyyMMdd}.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
    }
}

/// <summary>Per-device window and shortcut settings for the Windows client.</summary>
public sealed class ClientPrefs
{
    public int? X { get; set; }
    public int? Y { get; set; }
    public int Width { get; set; } = 1400;
    public int Height { get; set; } = 900;
    public bool Maximized { get; set; }
    public string CaptureHotkey { get; set; } = "Ctrl+Alt+Q";
    public string ScratchpadHotkey { get; set; } = "Ctrl+Alt+J";
    public bool ScratchpadOnTop { get; set; } = true;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static ClientPrefs Load(ClientPaths paths)
    {
        try { return File.Exists(paths.WindowFile) ? JsonSerializer.Deserialize<ClientPrefs>(File.ReadAllText(paths.WindowFile), Json) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
    }

    public void Save(ClientPaths paths)
    {
        try
        {
            Directory.CreateDirectory(paths.Settings);
            File.WriteAllText(paths.WindowFile, JsonSerializer.Serialize(this, Json));
        }
        catch (IOException) { }
    }
}
