using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Quantos.Core.Storage;

/// <summary>The vault's folder layout and naming rules (BUILD-PLAN section 4, STORAGE-SPEC sections 1 and 4).</summary>
public sealed partial class Vault
{
    public const string DefaultWorkspace = "default";

    public Vault(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }
    public string Workspaces => Path.Combine(Root, "workspaces");
    public string Attachments => Path.Combine(Root, "attachments");
    public string GlobalSupertags => Path.Combine(Root, "supertags");
    public string Templates => Path.Combine(Root, "templates");
    public string SyncedBlocks => Path.Combine(Root, "synced_blocks");
    public string Trash => Path.Combine(Root, ".trash");
    public string History => Path.Combine(Root, ".history");
    public string State => Path.Combine(Root, ".state");
    public string Journal => Path.Combine(State, "journal");
    public string Notifications => Path.Combine(State, "notifications.jsonl");
    public string LockFile => Path.Combine(State, "backend.lock");
    public string Plugins => Path.Combine(Root, "plugins");
    public string Themes => Path.Combine(Root, "themes");
    public string Exports => Path.Combine(Root, "exports");

    public static readonly string[] WorkspaceFolders = ["daily", "nodes", "tasks", "archive", "supertags"];
    public static readonly string[] ConfigFiles =
        ["workspaces.json", "settings.json", "automations.json", "shortcuts.json", "toolbar.json", "views.json", "task-dashboard.json", "dashboard.json"];

    public void EnsureLayout()
    {
        foreach (var dir in new[] { Workspaces, Attachments, GlobalSupertags, Templates, SyncedBlocks, Trash, History, State, Journal, Plugins, Themes, Exports })
            Directory.CreateDirectory(dir);
        EnsureWorkspace(DefaultWorkspace);
    }

    public string WorkspaceDir(string workspace) => Path.Combine(Workspaces, ValidateWorkspaceName(workspace));

    public void EnsureWorkspace(string workspace)
    {
        foreach (var folder in WorkspaceFolders) Directory.CreateDirectory(Path.Combine(WorkspaceDir(workspace), folder));
    }

    public IEnumerable<string> WorkspaceNames() =>
        Directory.Exists(Workspaces)
            ? Directory.EnumerateDirectories(Workspaces).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase)
            : [];

    public static string ValidateWorkspaceName(string name)
    {
        if (!WorkspaceName().IsMatch(name)) throw new ArgumentException($"Invalid workspace name '{name}'. Use lowercase letters, digits, '-' or '_'.");
        return name;
    }

    /// <summary>Folder for a new node of the given type.</summary>
    public string FolderFor(string workspace, string type, DateOnly? date = null) => type switch
    {
        NodeTypes.Daily => Path.Combine(WorkspaceDir(workspace), "daily", (date ?? DateOnly.FromDateTime(DateTime.Today)).Year.ToString(CultureInfo.InvariantCulture)),
        NodeTypes.Task => Path.Combine(WorkspaceDir(workspace), "tasks"),
        NodeTypes.Supertag => string.IsNullOrEmpty(workspace) ? GlobalSupertags : Path.Combine(WorkspaceDir(workspace), "supertags"),
        NodeTypes.Template => Templates,
        NodeTypes.SyncedBlock => SyncedBlocks,
        _ => Path.Combine(WorkspaceDir(workspace), "nodes")
    };

    public string DailyPath(string workspace, DateOnly date) =>
        Path.Combine(FolderFor(workspace, NodeTypes.Daily, date), date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".md");

    /// <summary>Lowercase, underscores, max 80 chars (STORAGE-SPEC section 4).</summary>
    public static string Slug(string title)
    {
        var normalized = title.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        }
        var slug = Underscores().Replace(sb.ToString(), "_").Trim('_');
        if (slug.Length > 80) slug = slug[..80].TrimEnd('_');
        if (slug.Length == 0) slug = "untitled";
        if (ReservedDeviceName().IsMatch(slug)) slug += "_";
        return slug;
    }

    /// <summary>First free path for <paramref name="slug"/> in <paramref name="folder"/>: name.md, name_2.md, ...</summary>
    public static string UniquePath(string folder, string slug, string? except = null)
    {
        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(folder, (n == 1 ? slug : $"{slug}_{n}") + ".md");
            if (!File.Exists(candidate) || PathEquals(candidate, except)) return candidate;
        }
    }

    public static bool PathEquals(string? a, string? b) =>
        a is not null && b is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    public string Relative(string absolute) => Path.GetRelativePath(Root, absolute).Replace('\\', '/');

    public string Absolute(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(full)) throw new UnauthorizedAccessException("Path escapes the vault.");
        return full;
    }

    public bool IsInside(string fullPath) =>
        fullPath.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Workspace a vault-relative path belongs to; empty for global folders.</summary>
    public static string WorkspaceOf(string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        return parts.Length > 2 && parts[0] == "workspaces" ? parts[1] : "";
    }

    public static bool IsArchivedPath(string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        return parts.Length > 3 && parts[0] == "workspaces" && parts[2] == "archive";
    }

    /// <summary>Every Markdown file that is authoritative node content.</summary>
    public IEnumerable<string> EnumerateNodeFiles()
    {
        var roots = new[] { Workspaces, GlobalSupertags, Templates, SyncedBlocks };
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        foreach (var root in roots.Where(Directory.Exists))
        foreach (var file in Directory.EnumerateFiles(root, "*.md", options))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.') || name.EndsWith(".quantos-tmp", StringComparison.OrdinalIgnoreCase)) continue;
            yield return file;
        }
    }

    /// <summary>Names the sync service when the path is inside a cloud-synced folder (BUILD-PLAN section 4).</summary>
    public static string? SyncedFolderService(string path)
    {
        var full = Path.GetFullPath(path);
        foreach (var variable in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
        {
            var root = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(root) && full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) return "OneDrive";
        }
        var markers = new (string Segment, string Service)[]
        {
            ("OneDrive", "OneDrive"), ("Dropbox", "Dropbox"), ("Google Drive", "Google Drive"), ("My Drive", "Google Drive"),
            ("iCloudDrive", "iCloud Drive"), ("iCloud Drive", "iCloud Drive"), ("Box", "Box"), ("pCloud Drive", "pCloud")
        };
        var segments = full.Split(Path.DirectorySeparatorChar);
        foreach (var segment in segments)
        foreach (var (marker, service) in markers)
            if (segment.Equals(marker, StringComparison.OrdinalIgnoreCase) || segment.StartsWith(marker + " - ", StringComparison.OrdinalIgnoreCase))
                return service;
        return null;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    private static partial Regex WorkspaceName();

    [GeneratedRegex("_+")]
    private static partial Regex Underscores();

    [GeneratedRegex("^(con|prn|aux|nul|com[0-9]|lpt[0-9])$")]
    private static partial Regex ReservedDeviceName();
}
