namespace Quantos.Core;

/// <summary>
/// Ensures one backend process owns a vault (BUILD-PLAN.md 3.E). Holds an exclusive handle on
/// Vault/.state/backend.lock for the life of the process. The OS releases it if the process dies.
/// </summary>
public sealed class VaultLock : IDisposable
{
    private readonly FileStream _stream;

    private VaultLock(FileStream stream) => _stream = stream;

    public static VaultLock Acquire(VaultLayout layout)
    {
        Directory.CreateDirectory(layout.State);
        try
        {
            var stream = new FileStream(layout.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(0);
            using (var writer = new StreamWriter(stream, leaveOpen: true))
                writer.Write($"pid={Environment.ProcessId}\nstarted={Timestamps.Format(DateTimeOffset.Now)}\n");
            stream.Flush(true);
            return new VaultLock(stream);
        }
        catch (IOException)
        {
            throw new VaultLockedException(layout.Root);
        }
    }

    public void Dispose() => _stream.Dispose();
}
