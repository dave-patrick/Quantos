using Quantos.Core;

namespace Quantos.Tests;

/// <summary>A throwaway vault + client-data folder per test.</summary>
public sealed class TestVault : IDisposable
{
    public TestVault()
    {
        Root = Path.Combine(Path.GetTempPath(), "quantos-tests", Guid.NewGuid().ToString("N"));
        VaultPath = Path.Combine(Root, "Vault");
        ClientData = Path.Combine(Root, "client-data");
        Layout = new VaultLayout(VaultPath);
        Cache = new NodeCache(Path.Combine(ClientData, "cache", "cache.sqlite"));
        Writes = new WriteService(Layout, Cache);
        Writes.Start();
    }

    public string Root { get; }
    public string VaultPath { get; }
    public string ClientData { get; }
    public VaultLayout Layout { get; }
    public NodeCache Cache { get; }
    public WriteService Writes { get; }

    public string FullPath(string rel) => Layout.ResolveInside(rel);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Root, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
