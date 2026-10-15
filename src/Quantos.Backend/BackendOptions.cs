using System.Security.Cryptography;

namespace Quantos.Backend;

/// <summary>Command-line options. Every option has a safe default so double-clicking the exe just works.</summary>
public sealed class BackendOptions
{
    public const int DefaultPort = 47821;

    public string VaultPath { get; init; } = Quantos.Core.VaultLocation.DefaultVaultPath();
    public string ClientDataPath { get; init; } = DefaultClientDataPath();
    public int Port { get; init; } = DefaultPort;
    public bool OpenBrowser { get; init; } = true;

    /// <summary>New random token at every launch (BUILD-PLAN.md 10, safeguard 8). Never logged.</summary>
    public string Token { get; init; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static BackendOptions Parse(string[] args)
    {
        string? vault = null, data = null;
        int? port = null;
        var openBrowser = true;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            switch (args[i])
            {
                case "--vault": vault = Next(); break;
                case "--client-data": data = Next(); break;
                case "--port": port = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--no-browser": openBrowser = false; break;
                default: break; // Ignore unknown args (ASP.NET passes some of its own).
            }
        }
        return new BackendOptions
        {
            VaultPath = Path.GetFullPath(vault ?? Quantos.Core.VaultLocation.DefaultVaultPath()),
            ClientDataPath = Path.GetFullPath(data ?? DefaultClientDataPath()),
            Port = port ?? DefaultPort,
            OpenBrowser = openBrowser,
        };
    }

    /// <summary>
    /// Portable layout from BUILD-PLAN.md 4: Quantos/application/ (the exes) next to Quantos/client-data/.
    /// If the exe isn't in a folder named "application", client-data goes next to the exe.
    /// </summary>
    public static string DefaultClientDataPath()
    {
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(appDir);
        return string.Equals(Path.GetFileName(appDir), "application", StringComparison.OrdinalIgnoreCase) && parent is not null
            ? Path.Combine(parent, "client-data")
            : Path.Combine(appDir, "client-data");
    }
}
