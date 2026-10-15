namespace Quantos.Core;

/// <summary>Default locations and safety checks for choosing a vault (BUILD-PLAN.md section 4, vault rules).</summary>
public static class VaultLocation
{
    /// <summary>C:\Users\&lt;you&gt;\Quantos\Vault on Windows; ~/Quantos/Vault elsewhere.</summary>
    public static string DefaultVaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Quantos", "Vault");

    /// <summary>
    /// Returns plain-language warnings for a vault path. A synced folder is allowed but warned about,
    /// because sync clients cause file locks, conflict copies and corruption.
    /// </summary>
    public static IReadOnlyList<string> Warnings(string vaultPath, Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        var full = Path.GetFullPath(vaultPath);
        var warnings = new List<string>();

        var syncRoots = new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" }
            .Select(getEnv).Where(v => !string.IsNullOrWhiteSpace(v)).Cast<string>();
        var underSyncRoot = syncRoots.Any(root => VaultLayout.IsInside(root, full));

        var segments = full.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var syncedName = segments.FirstOrDefault(s =>
            s.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Dropbox", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Google Drive", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("iCloudDrive", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Box", StringComparison.OrdinalIgnoreCase));

        if (underSyncRoot || syncedName is not null)
        {
            warnings.Add(
                $"This vault is inside a synced folder ({syncedName ?? "OneDrive"}). Sync software can lock files while Quantos saves, " +
                "create conflict copies, and corrupt notes if two devices change the same file. A folder that isn't synced, such as " +
                $"{DefaultVaultPath()}, is safer.");
        }
        return warnings;
    }

    /// <summary>The cache must never be inside the vault (acceptance test: storage 8).</summary>
    public static void EnsureCacheOutsideVault(string vaultPath, string clientDataPath)
    {
        if (VaultLayout.IsInside(vaultPath, clientDataPath))
            throw new ValidationException("The client-data folder (which holds the cache) can't be inside the vault. Choose a different location.");
    }
}
