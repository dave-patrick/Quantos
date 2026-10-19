using System.Security.Cryptography;
using System.Text.Json;
using Quantos.Core.Security;

namespace Quantos.Core.Storage;

public sealed record AttachmentInfo(string Path, string Hash, long Size, int References, DateTimeOffset Modified, string Extension);

/// <summary>
/// Global attachments (BUILD-PLAN 8D.6, 8D.9): stored once by content hash under
/// attachments/&lt;2 hex&gt;/&lt;sha256&gt;.&lt;ext&gt;, so the same file added twice is kept once.
/// Orphans (referenced by no note) can be moved to the trash, never deleted outright.
/// </summary>
public sealed class AttachmentStore(WriteService writes)
{
    private Vault Vault => writes.Vault;
    private static readonly HashSet<string> Allowed =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".pdf", ".txt", ".csv", ".md", ".docx", ".xlsx", ".pptx", ".zip", ".mp3", ".m4a", ".wav", ".webm", ".ogg", ".opus", ".mp4", ".json"];

    public (string Path, bool Existed) Add(Stream content, string fileName, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.CreateEdit, $"Add attachment {fileName}");
        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        if (!Allowed.Contains(ext)) throw new QuantosException($"Files of type {ext} can't be attached.");
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        if (buffer.Length > 200 * 1024 * 1024) throw new QuantosException("Attachments are limited to 200 MB.");
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var rel = $"attachments/{hash[..2]}/{hash}{ext}";
        var abs = Vault.Absolute(rel);
        if (File.Exists(abs)) return (rel, true);
        AtomicFile.WriteAllBytes(abs, bytes);
        return (rel, false);
    }

    public IReadOnlyList<AttachmentInfo> Audit()
    {
        if (!Directory.Exists(Vault.Attachments)) return [];
        var refs = writes.Cache.AttachmentRefCounts().ToDictionary(r => r.Path, r => r.Count, StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(Vault.Attachments, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(AtomicFile.TempSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(f => new FileInfo(f))
            .Select(f =>
            {
                var rel = Vault.Relative(f.FullName);
                return new AttachmentInfo(rel, System.IO.Path.GetFileNameWithoutExtension(f.Name), f.Length, refs.GetValueOrDefault(rel), f.LastWriteTimeUtc, f.Extension.ToLowerInvariant());
            })
            .OrderBy(a => a.References).ThenByDescending(a => a.Size).ToList();
    }

    /// <summary>Moves an unreferenced attachment to the trash (restorable like any other item).</summary>
    public TrashEntry TrashOrphan(string relativePath, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.TrashRestore, $"Move {relativePath} to trash");
        var abs = Vault.Absolute(relativePath);
        if (!abs.StartsWith(Vault.Attachments, StringComparison.OrdinalIgnoreCase) || !File.Exists(abs)) throw new QuantosException("Attachment not found.", 404);
        if (writes.Cache.AttachmentRefCounts().Any(r => string.Equals(r.Path, relativePath, StringComparison.OrdinalIgnoreCase) && r.Count > 0))
            throw new QuantosException("Notes still use this attachment.", 409);
        return writes.Exclusive(() =>
        {
            var (dir, dest) = writes.TrashBin.Plan(System.IO.Path.GetFileNameWithoutExtension(abs)[..26].ToUpperInvariant(), abs);
            var entry = new TrashEntry
            {
                EntryId = System.IO.Path.GetFileName(dir), Id = System.IO.Path.GetFileNameWithoutExtension(abs), Title = System.IO.Path.GetFileName(abs), Type = "attachment",
                OriginalPath = relativePath.Replace('\\', '/'), FileName = System.IO.Path.GetFileName(abs), TrashedAt = writes.Clock.Now
            };
            var op = new JournalOperation { Description = $"Trash attachment {relativePath}" };
            op.Steps.Add(JournalStep.Write(Vault.Relative(System.IO.Path.Combine(dir, "manifest.json")), null, JsonSerializer.Serialize(entry, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })));
            op.Steps.Add(JournalStep.Move(relativePath, Vault.Relative(dest)));
            writes.Journal.Execute(op);
            writes.Changes.Publish(new ChangeEvent(ChangeKind.Trashed, null, null, relativePath, Origin: actor.ToString(), Data: entry));
            return entry;
        });
    }
}
