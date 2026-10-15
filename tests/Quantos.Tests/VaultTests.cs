using Quantos.Core;

namespace Quantos.Tests;

public class VaultTests
{
    [Fact]
    public void Vault_has_the_planned_folder_structure()
    {
        using var v = new TestVault();
        foreach (var rel in new[] { "workspaces/default/daily", "workspaces/default/nodes", "workspaces/default/tasks", "workspaces/default/archive",
                     "workspaces/default/supertags", "attachments", "supertags", "templates", "synced_blocks", ".trash", ".history", ".state/journal",
                     "plugins", "themes", "exports" })
            Assert.True(Directory.Exists(v.FullPath(rel)), rel);
        foreach (var rel in new[] { "settings.json", "workspaces.json", ".state/notifications.jsonl", "workspaces/default/workspace-state.json" })
            Assert.True(File.Exists(v.FullPath(rel)), rel);
    }

    [Fact]
    public void Cache_lives_outside_the_vault()
    {
        using var v = new TestVault();
        Assert.False(VaultLayout.IsInside(v.VaultPath, v.Cache.FilePath));
        Assert.Empty(Directory.GetFiles(v.VaultPath, "*.sqlite*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Client_data_inside_the_vault_is_refused()
    {
        Assert.Throws<ValidationException>(() =>
            VaultLocation.EnsureCacheOutsideVault(@"/home/me/Vault", @"/home/me/Vault/client-data"));
        VaultLocation.EnsureCacheOutsideVault(@"/home/me/Vault", @"/home/me/VaultData/client-data");
    }

    [Fact]
    public void OneDrive_vault_shows_a_warning()
    {
        var oneDrive = Path.Combine(Path.GetTempPath(), "OneDrive - KPMG");
        string? Env(string k) => k == "OneDriveCommercial" ? oneDrive : null;

        var warnings = VaultLocation.Warnings(Path.Combine(oneDrive, "Quantos", "Vault"), Env);
        Assert.Contains(warnings, w => w.Contains("synced folder") && w.Contains("conflict copies") && w.Contains("corrupt"));

        Assert.Empty(VaultLocation.Warnings(Path.Combine(Path.GetTempPath(), "Quantos", "Vault"), Env));
    }

    [Fact]
    public void A_second_backend_cannot_open_the_same_vault()
    {
        using var v = new TestVault();
        using var first = VaultLock.Acquire(v.Layout);
        Assert.Throws<VaultLockedException>(() => VaultLock.Acquire(v.Layout));
        first.Dispose();
        using var again = VaultLock.Acquire(v.Layout);
    }

    [Theory]
    [InlineData("Project Kickoff!", "project_kickoff")]
    [InlineData("Café résumé", "cafe_resume")]
    [InlineData("  ---  ", "untitled")]
    [InlineData("Q4/Q1 plan: v2", "q4_q1_plan_v2")]
    public void Filenames_are_lowercase_with_underscores(string title, string expected) =>
        Assert.Equal(expected, WriteService.Slug(title));
}
