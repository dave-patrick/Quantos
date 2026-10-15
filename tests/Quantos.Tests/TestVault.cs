using Quantos.Core;

namespace Quantos.Tests;

/// <summary>A throwaway vault and client-data folder. Each test gets its own.</summary>
public sealed class TestVault : IDisposable
{
    public TestVault(bool start = true)
    {
        Root = Path.Combine(Path.GetTempPath(), "quantos-tests", Guid.NewGuid().ToString("N"));
        VaultPath = Path.Combine(Root, "Vault");
        ClientData = Path.Combine(Root, "client-data");
        Session = Open();
        if (start) Session.Start();
    }

    public string Root { get; }
    public string VaultPath { get; }
    public string ClientData { get; }
    public VaultSession Session { get; private set; }
    public WriteService Writes => Session.Writes;

    public VaultSession Open() => new(VaultPath, new QuantosPaths(ClientData));

    /// <summary>Simulates stopping and starting the backend.</summary>
    public ReconcileReport Restart()
    {
        Session.Dispose();
        Session = Open();
        return Session.Start();
    }

    public string Abs(string relative) => Path.Combine(VaultPath, relative.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        Session.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
