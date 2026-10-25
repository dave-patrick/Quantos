using System.Text.Json;
using Quantos.Core.Storage;

namespace Quantos.Core.Sync;

/// <summary>A narrow, opt-in bridge from an externally managed Markdown folder to one shared workspace.
/// State, conflicts and recovery stay outside the source; original bytes pass through WriteService.</summary>
public sealed class FolderBridge
{
    private readonly VaultSession session;
    private readonly string state;
    private readonly ScopedRemote remote;
    public SyncEngine Engine { get; }

    public FolderBridge(VaultSession session, string sourceFolder, string workspace, string stateFolder, ISyncRemote remote)
    {
        this.session = session;
        state = Path.GetFullPath(stateFolder);
        var source = Path.GetFullPath(sourceFolder);
        if (state.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Vault.PathEquals(source, state))
            throw new QuantosException("Bridge state must be outside the linked source folder.");
        Directory.CreateDirectory(state);
        var binding = JsonSerializer.Serialize(new { source, workspace = Vault.ValidateWorkspaceName(workspace), mapping = "workspace-relative-v1" });
        var bindingFile = Path.Combine(state, "folder-binding.json");
        if (File.Exists(bindingFile) && File.ReadAllText(bindingFile) != binding)
            throw new QuantosException("This bridge state belongs to another source folder or workspace. Use a separate state folder.");
        session.Vault.RegisterLinkedFolder(workspace, source);
        session.Vault.SyncLinkedFolders.Add(workspace);
        AtomicFile.WriteAllText(bindingFile, binding);
        this.remote = new ScopedRemote(remote, $"workspaces/{workspace}/");
        // Remove clean old paths before adding renamed paths, so an indexer never sees both
        // names with the same note identity. Concurrent edit-versus-delete still becomes a conflict.
        Engine = new SyncEngine(session.Writes, state, this.remote, deletionsFirst: true);
    }

    public async Task<SyncReport> Run(CancellationToken ct = default)
    {
        // A disconnected cloud drive, lost server folder or large batch of removals must never look
        // like an instruction to erase the other copy. An operator reviews these cases explicitly.
        var local = SyncFiles.Manifest(session.Writes).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var server = (await remote.Manifest(ct)).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var checkpoint = Path.Combine(state, "checkpoint.json");
        if (File.Exists(checkpoint))
        {
            var baseline = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(checkpoint))!;
            var known = baseline.Where(p => p.Value is not null).Select(p => p.Key).ToList();
            var threshold = Math.Max(10, (int)Math.Ceiling(known.Count * .1));
            if (known.Count > 0 && (server.Count == 0 || known.Count(p => !local.Contains(p)) >= threshold || known.Count(p => !server.Contains(p)) >= threshold))
                throw new QuantosException("Folder sync paused: a folder is unavailable or many notes were removed. Review the source, server and recovery copies before continuing.", 409);
        }
        return await Engine.Run(ct);
    }

    private sealed class ScopedRemote(ISyncRemote inner, string prefix) : ISyncRemote
    {
        private bool Includes(string path) => path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        public async Task<IReadOnlyList<SyncFile>> Manifest(CancellationToken ct) => (await inner.Manifest(ct)).Where(f => Includes(f.Path)).ToList();
        public Task<byte[]?> Read(string path, string? hash, CancellationToken ct) => Includes(path) ? inner.Read(path, hash, ct) : throw new QuantosException("That file is outside the linked folder.");
        public Task<bool> Apply(SyncMutation mutation, CancellationToken ct) => Includes(mutation.Path) ? inner.Apply(mutation, ct) : throw new QuantosException("That file is outside the linked folder.");
    }
}
