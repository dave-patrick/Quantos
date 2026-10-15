using System.Text.Json;

namespace Quantos.Core.Storage;

/// <summary>
/// Exclusive lock held for as long as a backend owns the vault, so a second backend can never
/// become a second writer (BUILD-PLAN section 3E). The OS releases it if the process dies.
/// </summary>
public sealed class VaultLock : IDisposable
{
    private readonly FileStream _stream;

    private VaultLock(FileStream stream, string path)
    {
        _stream = stream;
        Path = path;
    }

    public string Path { get; }

    public static VaultLock Acquire(Vault vault)
    {
        Directory.CreateDirectory(vault.State);
        FileStream stream;
        try
        {
            stream = new FileStream(vault.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            var owner = ReadOwner(vault);
            throw new VaultLockedException(vault.Root, owner);
        }
        var info = JsonSerializer.SerializeToUtf8Bytes(new { pid = Environment.ProcessId, machine = Environment.MachineName, started = DateTimeOffset.Now });
        stream.SetLength(0);
        stream.Write(info);
        stream.Flush(true);
        return new VaultLock(stream, vault.LockFile);
    }

    public static string? ReadOwner(Vault vault)
    {
        try
        {
            using var s = new FileStream(vault.LockFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Dispose() => _stream.Dispose();
}

public sealed class VaultLockedException(string vault, string? owner)
    : Exception($"Another Quantos backend already owns the vault at {vault}.")
{
    public string? Owner { get; } = owner;
}
