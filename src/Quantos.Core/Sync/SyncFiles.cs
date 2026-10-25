using System.Security.Cryptography;
using Quantos.Core.Security;
using Quantos.Core.Storage;

namespace Quantos.Core.Sync;

public sealed record SyncFile(string Path, string Hash, long Size);
public sealed record SyncMutation(string Path, string? ExpectedHash, byte[]? Content);
public sealed record SyncConflict(string Id, string Path, string? BaseHash, string? LocalHash, string? RemoteHash, DateTimeOffset Detected);
public sealed record SyncReport(int Uploaded, int Downloaded, int Conflicts);

/// <summary>Only portable vault content crosses devices. Locks, keys, caches and linked paths never do.</summary>
public static class SyncFiles
{
    public const long MaxFileBytes = 200L * 1024 * 1024;
    private static readonly HashSet<string> Roots = ["workspaces", "attachments", "templates", "supertags", "synced_blocks", "themes", "plugins", ".trash"];
    private static readonly HashSet<string> Configs = Vault.ConfigFiles.Where(f => f != Vault.LinkedFoldersFile).ToHashSet();
    public static bool IsDeviceState(string path)
    {
        var parts = path.Split('/');
        return parts.Length == 3 && parts[0] == "workspaces" && parts[2] == "workspace-state.json";
    }

    public static string Validate(Vault vault, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/'))
            throw new QuantosException("Invalid sync path.");
        var parts = path.Split('/');
        if (IsDeviceState(path)) throw new QuantosException("Workspace layout belongs to this device and is not synced.");
        if (parts.Any(p => p is "" or "." or ".." || p.Any(char.IsControl) || p.EndsWith('.') || p.EndsWith(' ')))
            throw new QuantosException("Invalid sync path.");
        if (!(parts.Length == 1 && Configs.Contains(path)) && !(parts.Length > 1 && Roots.Contains(parts[0])))
            throw new QuantosException("This file belongs to the device and cannot be synced.");
        if (path.EndsWith(AtomicFile.TempSuffix, StringComparison.OrdinalIgnoreCase)) throw new QuantosException("Temporary files cannot be synced.");
        var full = vault.Absolute(path);
        // Do not let a junction or symlink turn a portable vault path into outside-file access.
        for (var item = new FileInfo(full) as FileSystemInfo; item is not null; item = item is FileInfo f ? f.Directory : ((DirectoryInfo)item).Parent)
        {
            if (item.LinkTarget is not null || item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new QuantosException("Linked files cannot be synced.");
            if (string.Equals(item.FullName, vault.Root, StringComparison.OrdinalIgnoreCase)) break;
        }
        return full;
    }

    public static string Hash(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
    public static string? HashFile(string full)
    {
        if (!File.Exists(full)) return null;
        using var stream = File.OpenRead(full);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static IReadOnlyList<SyncFile> Manifest(WriteService writes) => writes.Exclusive(() =>
    {
        var entries = new List<SyncFile>();
        void Add(string file)
        {
            var path = writes.Vault.Relative(file);
            try { Validate(writes.Vault, path); }
            catch (QuantosException) { return; }
            var size = new FileInfo(file).Length;
            if (size > MaxFileBytes) throw new QuantosException($"{path} is too large to sync.");
            entries.Add(new SyncFile(path, HashFile(file)!, size));
            if (entries.Count > 100000) throw new QuantosException("The vault has too many files for this sync version.");
        }
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        foreach (var root in Roots)
        {
            var dir = Path.Combine(writes.Vault.Root, root);
            if (Directory.Exists(dir) && (File.GetAttributes(dir) & FileAttributes.ReparsePoint) == 0)
                foreach (var file in Directory.EnumerateFiles(dir, "*", options)) Add(file);
        }
        foreach (var config in Configs)
        {
            var file = Path.Combine(writes.Vault.Root, config);
            if (File.Exists(file)) Add(file);
        }
        return (IReadOnlyList<SyncFile>)entries;
    });
}
