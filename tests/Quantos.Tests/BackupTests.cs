using System.IO.Compression;
using Quantos.Core;
using Quantos.Core.Backups;
using Quantos.Core.Security;

namespace Quantos.Tests;

/// <summary>Release 1.1 rolling backups: complete, checked, pruned, and restorable without overwriting.</summary>
public class BackupTests
{
    private static BackupService B(TestVault v) => v.Session.Backups;

    [Fact]
    public async Task A_backup_holds_the_vault_and_checks_out()
    {
        using var v = new TestVault();
        var note = v.Writes.Create(new CreateNodeRequest { Title = "Plan", Body = "Keep this.\n" }, Actor.User);
        var backup = await B(v).BackUpAsync(Actor.User);
        Assert.True(File.Exists(backup.Path));
        Assert.Empty(B(v).Verify(backup.Name, Actor.User));
        using var zip = ZipFile.OpenRead(backup.Path);
        Assert.Contains(zip.Entries, e => e.FullName == note.Path);
        Assert.Contains(zip.Entries, e => e.FullName == BackupService.ManifestName);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith(".history/") || e.FullName.EndsWith("backend.lock"));
        Assert.Empty(Directory.EnumerateFiles(B(v).Folder, "*.partial"));
    }

    [Fact]
    public async Task A_damaged_backup_is_reported()
    {
        using var v = new TestVault();
        v.Writes.Create(new CreateNodeRequest { Title = "Plan", Body = "Original.\n" }, Actor.User);
        var backup = await B(v).BackUpAsync(Actor.User);
        using (var zip = ZipFile.Open(backup.Path, ZipArchiveMode.Update))
        {
            var entry = zip.Entries.First(e => e.FullName.EndsWith(".md"));
            using var s = entry.Open();
            s.SetLength(0);
            s.Write("tampered"u8);
        }
        Assert.Contains(B(v).Verify(backup.Name, Actor.User), p => p.Contains("damaged"));
    }

    [Fact]
    public async Task Only_the_newest_backups_are_kept()
    {
        using var v = new TestVault();
        B(v).SaveSettings(new BackupSettings { Keep = 2 });
        // A stranger's zip in the same folder is never touched.
        Directory.CreateDirectory(B(v).Folder);
        var stranger = Path.Combine(B(v).Folder, "my-own-archive.zip");
        File.WriteAllText(stranger, "not ours");
        for (var i = 0; i < 3; i++) { await B(v).BackUpAsync(Actor.User); await Task.Delay(1100); }
        Assert.Equal(2, B(v).List().Count);
        Assert.True(File.Exists(stranger));
    }

    [Fact]
    public async Task Restoring_a_note_keeps_the_current_text_in_history()
    {
        using var v = new TestVault();
        var note = v.Writes.Create(new CreateNodeRequest { Title = "Plan", Body = "First version.\n" }, Actor.User);
        var backup = await B(v).BackUpAsync(Actor.User);
        var current = v.Writes.Require(note.Id);
        var saved = v.Writes.Save(note.Id, current.Content.Replace("First version.", "Second version."), current.Hash, Actor.User);
        Assert.True(v.Writes.Require(note.Id).Body.Contains("Second version."), saved.Status + " " + saved.Message);

        var listed = B(v).Notes(backup.Name, Actor.User).Single(n => n.Title == "Plan");
        Assert.True(listed.ExistsNow);
        B(v).RestoreNote(backup.Name, listed.Path, Actor.User);
        Assert.Contains("First version.", v.Writes.Require(note.Id).Body);
        var versions = v.Writes.History.List(note.Id).Select(x => v.Writes.History.Read(note.Id, x.Version)!);
        Assert.True(versions.Any(c => c.Contains("Second version.")), string.Join("\n=====\n", versions));

        // A note deleted since comes back where it was.
        v.Writes.Trash(note.Id, Actor.User);
        Assert.False(B(v).Notes(backup.Name, Actor.User).Single(n => n.Title == "Plan").ExistsNow);
    }

    [Fact]
    public async Task A_whole_backup_restores_to_a_new_folder_never_over_one()
    {
        using var v = new TestVault();
        v.Writes.Create(new CreateNodeRequest { Title = "Plan", Body = "Keep this.\n" }, Actor.User);
        var backup = await B(v).BackUpAsync(Actor.User);
        var target = Path.Combine(v.Root, "Restored");
        var folder = B(v).RestoreToFolder(backup.Name, target, Actor.User);
        Assert.Equal(target, folder);
        Assert.Contains(Directory.EnumerateFiles(target, "*.md", SearchOption.AllDirectories), f => File.ReadAllText(f).Contains("Keep this."));
        Assert.False(File.Exists(Path.Combine(target, BackupService.ManifestName)));
        Assert.Throws<QuantosException>(() => B(v).RestoreToFolder(backup.Name, target, Actor.User)); // not empty now
    }

    [Fact]
    public void Backups_cannot_be_kept_inside_the_vault()
    {
        using var v = new TestVault();
        var ex = Assert.Throws<QuantosException>(() => B(v).SaveSettings(new BackupSettings { Folder = Path.Combine(v.VaultPath, "backups") }));
        Assert.Contains("outside the vault", ex.Message);
        Assert.Throws<QuantosException>(() => B(v).SaveSettings(new BackupSettings { Keep = 0 }));
    }
}
