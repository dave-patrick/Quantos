using System.Text.Json;

namespace Quantos.Core;

/// <summary>
/// Paths inside a vault, and creation of the standard folder structure from BUILD-PLAN.md section 4.
/// The vault holds authoritative content only. Derived data (the cache) must live outside it.
/// </summary>
public sealed class VaultLayout
{
    public const int SchemaVersion = 1;
    public const string DefaultWorkspace = "default";

    public VaultLayout(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }
    public string Workspaces => Path.Combine(Root, "workspaces");
    public string Trash => Path.Combine(Root, ".trash");
    public string History => Path.Combine(Root, ".history");
    public string State => Path.Combine(Root, ".state");
    public string Journal => Path.Combine(State, "journal");
    public string LockFile => Path.Combine(State, "backend.lock");

    public string Workspace(string name) => Path.Combine(Workspaces, name);

    public static readonly string[] WorkspaceFolders = ["daily", "nodes", "tasks", "archive", "supertags"];

    private static readonly string[] RootFolders =
        ["attachments", "supertags", "templates", "synced_blocks", ".trash", ".history", ".state", ".state/journal", "plugins", "themes", "exports"];

    /// <summary>Creates any missing folders and starter files. Never overwrites an existing file.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        foreach (var folder in RootFolders) Directory.CreateDirectory(Path.Combine(Root, folder));
        EnsureWorkspace(DefaultWorkspace);

        WriteIfMissing(Path.Combine(State, "notifications.jsonl"), "");
        WriteJsonIfMissing("settings.json", new { schemaVersion = SchemaVersion });
        WriteJsonIfMissing("workspaces.json", new { schemaVersion = SchemaVersion, workspaces = new[] { new { name = DefaultWorkspace } } });
        foreach (var name in new[] { "automations.json", "shortcuts.json", "toolbar.json", "views.json", "task-dashboard.json", "dashboard.json" })
            WriteJsonIfMissing(name, new { schemaVersion = SchemaVersion });
    }

    public void EnsureWorkspace(string name)
    {
        ValidateWorkspaceName(name);
        foreach (var folder in WorkspaceFolders) Directory.CreateDirectory(Path.Combine(Workspace(name), folder));
        WriteIfMissing(Path.Combine(Workspace(name), "workspace-state.json"), "{}\n");
    }

    public IReadOnlyList<string> ListWorkspaces() =>
        Directory.Exists(Workspaces)
            ? Directory.GetDirectories(Workspaces).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    public static void ValidateWorkspaceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            throw new ValidationException("Workspace names use letters, numbers, '-' and '_' only (max 64).");
    }

    /// <summary>Resolves a vault-relative path and refuses anything that escapes the vault.</summary>
    public string ResolveInside(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!IsInside(Root, full)) throw new ValidationException("Path is outside the vault.");
        return full;
    }

    public string Relative(string fullPath) => Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

    public static bool IsInside(string parent, string candidate)
    {
        var p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var c = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return c.StartsWith(p, comparison);
    }

    private void WriteJsonIfMissing(string fileName, object value) =>
        WriteIfMissing(Path.Combine(Root, fileName), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n");

    private static void WriteIfMissing(string path, string content)
    {
        if (File.Exists(path)) return;
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream);
            writer.Write(content);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Created by someone else in the meantime; keep theirs.
        }
    }
}
