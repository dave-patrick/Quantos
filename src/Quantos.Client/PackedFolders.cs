using System.IO.Compression;
using System.Security.Cryptography;

namespace Quantos.Client;

/// <summary>
/// The three programs share one set of runtime files in the application folder (rebuild 1, 2026-11-07),
/// instead of each carrying its own copy inside a single-file .exe. A few of those files live in
/// subfolders (PowerShell's modules and reference assemblies). Updaters already installed on people's
/// machines copy only the files directly in application\, so the build packs the subfolders into
/// <see cref="ZipName"/> and the client puts them back here before anything needs them.
/// </summary>
internal static class PackedFolders
{
    public const string ZipName = "quantos-folders.zip";
    public const string StampName = "quantos-folders.stamp";

    /// <summary>Unpacks the folders when the zip is new or changed. Returns false if that failed (logged; the app still starts).</summary>
    public static bool EnsureUnpacked(string appDirectory, Action<string>? log = null)
    {
        var zip = Path.Combine(appDirectory, ZipName);
        if (!File.Exists(zip)) return true; // a development build, or nothing packed
        var stampFile = Path.Combine(appDirectory, StampName);
        try
        {
            string hash;
            using (var stream = File.OpenRead(zip)) hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (File.Exists(stampFile) && File.ReadAllText(stampFile).Trim() == hash) return true;

            var root = Path.GetFullPath(appDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue; // folder entries
                    var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    // Only into subfolders of the application folder: never above it, never over a program file.
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(target)!.Length < root.Length) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }
            File.WriteAllText(stampFile, hash);
            log?.Invoke("Unpacked the program's support folders.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log?.Invoke("Couldn't unpack the program's support folders: " + ex.Message);
            return false;
        }
    }
}
