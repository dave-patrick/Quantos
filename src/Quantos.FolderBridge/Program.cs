using System.Text.Json;
using Quantos.Core;
using Quantos.Core.Sync;

string? statePath = null;
try
{
    string? Option(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    string Required(string name) => Option(name) ?? throw new ArgumentException($"Missing {name}.");
    var folder = Path.GetFullPath(Required("--folder"));
    var workspace = Required("--workspace");
    var state = Path.GetFullPath(Required("--state"));
    statePath = state;
    Directory.CreateDirectory(state);
    using var singleInstance = new FileStream(Path.Combine(state, "bridge.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    using var session = new VaultSession(Path.Combine(state, "bridge-vault"), new QuantosPaths(Path.Combine(state, "client-data")));
    // Do not reconcile or watch the external source: only explicit exact sync writes may touch it.
    using var remote = new HttpSyncRemote(Required("--server"), File.ReadAllText(Required("--token-file")).Trim(), Path.Combine(state, "server-id"));
    var bridge = new FolderBridge(session, folder, workspace, Path.Combine(state, "sync"), remote);
    using var stop = new CancellationTokenSource();
    if (!OperatingSystem.IsWindows() || !Console.IsInputRedirected)
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    if (Option("--resolve") is { } conflict)
    {
        var keep = Required("--keep");
        if (keep is not ("folder" or "server")) throw new ArgumentException("Use --keep folder or --keep server.");
        await bridge.Engine.Resolve(conflict, keep == "folder", stop.Token);
    }
    var once = args.Contains("--once");
    do
    {
        try
        {
            var result = await bridge.Run(stop.Token);
            var status = new { ok = true, workspace, time = DateTimeOffset.Now, result.Uploaded, result.Downloaded, result.Conflicts };
            AtomicStatus(status);
            Console.WriteLine(JsonSerializer.Serialize(status));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Report failures without note bodies, server keys or request headers.
            var status = new { ok = false, workspace, time = DateTimeOffset.Now, error = ex.Message };
            AtomicStatus(status);
            Console.Error.WriteLine(JsonSerializer.Serialize(status));
            if (once) return 1;
        }
        if (!once) await Task.Delay(TimeSpan.FromSeconds(15), stop.Token);
    } while (!once);
    return 0;

    void AtomicStatus<T>(T status) => Quantos.Core.Storage.AtomicFile.WriteAllText(Path.Combine(state, "status.json"), JsonSerializer.Serialize(status));
}
catch (OperationCanceledException) { return 0; }
catch (Exception ex)
{
    if (statePath is not null)
        try { Quantos.Core.Storage.AtomicFile.WriteAllText(Path.Combine(statePath, "status.json"), JsonSerializer.Serialize(new { ok = false, time = DateTimeOffset.Now, error = ex.Message })); } catch (IOException) { }
    Console.Error.WriteLine($"Folder bridge stopped: {ex.Message}"); return 1;
}
