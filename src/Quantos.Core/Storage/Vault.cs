using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Quantos.Core.Storage;

/// <summary>The vault's folder layout and naming rules (BUILD-PLAN section 4, STORAGE-SPEC sections 1 and 4).</summary>
public sealed partial class Vault
{
    public const string DefaultWorkspace = "default";
    /// <summary>The path prefix a linked folder's files get inside the vault's own path space.</summary>
    public const string LinkedPrefix = "linked";
    /// <summary>The file in the vault that holds the folders linked in from elsewhere on this computer.</summary>
    public const string LinkedFoldersFile = "linked-folders.json";

    public Vault(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }
    /// <summary>Folders elsewhere on disk whose Markdown is indexed in place (STORAGE-SPEC section 9).
    /// The files stay where they are: the other app keeps writing them and Quantos reads and writes
    /// the same files. A linked folder is a scope of its own, named like a workspace but never the
    /// same as one, so the two can be told apart.</summary>
    public Dictionary<string, string> LinkedRoots { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Explicit folder-bridge opt-in only. Ordinary device links remain private and never sync.</summary>
    public HashSet<string> SyncLinkedFolders { get; } = new(StringComparer.OrdinalIgnoreCase);

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
        ["workspaces.json", "settings.json", "automations.json", "shortcuts.json", "toolbar.json", "views.json", "task-dashboard.json", "dashboard.json", LinkedFoldersFile];

    public void EnsureLayout()
    {
        foreach (var dir in new[] { Workspaces, Attachments, GlobalSupertags, Templates, SyncedBlocks, Trash, History, State, Journal, Plugins, Themes, Exports })
            Directory.CreateDirectory(dir);
        EnsureWorkspace(DefaultWorkspace);
    }

    /// <summary>True when the scope is a linked folder rather than a workspace of the vault.</summary>
    public bool IsLinked(string scope) => LinkedRoots.ContainsKey(scope);

    /// <summary>Names of the folders linked in from elsewhere on this computer.</summary>
    public IEnumerable<string> LinkedNames() => LinkedRoots.Keys.Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>The folder holding a scope's files. A linked folder is the folder itself.</summary>
    public string WorkspaceDir(string scope)
    {
        if (LinkedRoots.TryGetValue(scope, out var root)) return root;
        return Path.Combine(Workspaces, ValidateWorkspaceName(scope));
    }

    /// <summary>Where a scope's layout state is kept. A linked folder's stays in the vault: the other
    /// app's folder holds the owner's own files and nothing of Quantos' making.</summary>
    public string WorkspaceStatePath(string scope) =>
        IsLinked(scope)
            ? Path.Combine(State, "workspace-state", scope + ".json")
            : Path.Combine(WorkspaceDir(scope), "workspace-state.json");

    public void EnsureWorkspace(string scope)
    {
        // A linked folder belongs to another app: Quantos adds no folders of its own there.
        if (IsLinked(scope)) return;
        foreach (var folder in WorkspaceFolders) Directory.CreateDirectory(Path.Combine(WorkspaceDir(scope), folder));
    }

    /// <summary>Links a folder outside the vault. The files in it are read and written in place.</summary>
    public void RegisterLinkedFolder(string name, string path)
    {
        name = ValidateWorkspaceName(name);
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) throw new QuantosException("That folder doesn't exist.");
        // Neither folder may contain the other: the vault's files would then be indexed twice.
        if (IsInside(full) || IsInsideVault(full)) throw new QuantosException("Link a folder outside the vault.");
        if (Directory.Exists(Path.Combine(Workspaces, name))) throw new QuantosException($"The vault already has a workspace called '{name}'.");
        if (LinkedRoots.ContainsKey(name)) throw new QuantosException($"'{name}' is already linked.");
        // A folder inside another linked folder would have its files indexed twice.
        foreach (var (other, otherPath) in LinkedRoots)
        {
            var otherFull = Path.GetFullPath(otherPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(otherFull, StringComparison.OrdinalIgnoreCase)) throw new QuantosException($"That folder is inside the linked folder '{other}'.");
            if (string.Equals(Path.GetFullPath(otherPath), full, StringComparison.OrdinalIgnoreCase)) throw new QuantosException($"That folder is already linked as '{other}'.");
        }
        LinkedRoots[name] = full;
    }

    /// <summary>Unlinks a folder. The files stay where they are; only Quantos stops watching them.</summary>
    public void UnregisterLinkedFolder(string name) => LinkedRoots.Remove(name);

    /// <summary>True when the vault itself is inside the given folder.</summary>
    private bool IsInsideVault(string fullPath)
    {
        var full = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var root = Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return root.StartsWith(full, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The linked folder a vault-relative path belongs to, or null when it is an ordinary note.</summary>
    public string? LinkedFolderOf(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/');
        return parts.Length >= 2 && parts[0] == LinkedPrefix && LinkedRoots.ContainsKey(parts[1]) ? parts[1] : null;
    }

    /// <summary>The linked folder a path belongs to, from the path alone. Used where the vault's own
    /// list of linked folders isn't to hand, such as when a note is read.</summary>
    public static string? LinkedScopeOf(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/');
        return parts.Length >= 3 && parts[0] == LinkedPrefix ? parts[1] : null;
    }

    /// <summary>Names of the vault's own workspaces. A linked folder is not one of them.</summary>
    public IEnumerable<string> WorkspaceNames()
    {
        if (!Directory.Exists(Workspaces)) return [];
        return Directory.EnumerateDirectories(Workspaces).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase);
    }

    public static string ValidateWorkspaceName(string name)
    {
        if (!WorkspaceName().IsMatch(name)) throw new ArgumentException($"Invalid workspace name '{name}'. Use lowercase letters, digits, '-' or '_'.");
        return name;
    }

    /// <summary>Folder for a new node of the given type. A linked folder keeps its own layout: new
    /// files go straight into the folder, so the other app sees them next to its own notes.</summary>
    public string FolderFor(string scope, string type, DateOnly? date = null)
    {
        if (type == NodeTypes.Template) return Templates;
        if (type == NodeTypes.SyncedBlock) return SyncedBlocks;
        if (IsLinked(scope)) return WorkspaceDir(scope);
        return type switch
        {
            NodeTypes.Daily => Path.Combine(WorkspaceDir(scope), "daily", (date ?? DateOnly.FromDateTime(DateTime.Today)).Year.ToString(CultureInfo.InvariantCulture)),
            NodeTypes.Task => Path.Combine(WorkspaceDir(scope), "tasks"),
            NodeTypes.Supertag => string.IsNullOrEmpty(scope) ? GlobalSupertags : Path.Combine(WorkspaceDir(scope), "supertags"),
            _ => Path.Combine(WorkspaceDir(scope), "nodes")
        };
    }

    public string DailyPath(string scope, DateOnly date) =>
        Path.Combine(FolderFor(scope, NodeTypes.Daily, date), date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".md");

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

    /// <summary>The vault-relative path of a file on disk. Files in a linked folder are named under
    /// "linked/&lt;name&gt;/" so they never clash with the vault's own folders.</summary>
    public string Relative(string absolute)
    {
        var full = Path.GetFullPath(absolute);
        foreach (var (name, root) in LinkedRoots)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), rootFull, StringComparison.OrdinalIgnoreCase))
                return $"{LinkedPrefix}/{name}";
            if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = Path.GetRelativePath(root, full).Replace('\\', '/');
            return rel == "." ? $"{LinkedPrefix}/{name}" : $"{LinkedPrefix}/{name}/{rel}";
        }
        return Path.GetRelativePath(Root, full).Replace('\\', '/');
    }

    /// <summary>The file on disk a vault-relative path names.</summary>
    public string Absolute(string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0] == LinkedPrefix && LinkedRoots.TryGetValue(parts[1], out var root))
        {
            var rest = string.Join(Path.DirectorySeparatorChar, parts.Skip(2));
            var full = Path.GetFullPath(Path.Combine(root, rest));
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) && !string.Equals(full, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Path escapes the linked folder.");
            return full;
        }
        var full2 = Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(full2)) throw new UnauthorizedAccessException("Path escapes the vault.");
        return full2;
    }

    /// <summary>True when a path is inside the vault itself. Files in a linked folder are outside.</summary>
    public bool IsInside(string fullPath)
    {
        var full = Path.GetFullPath(fullPath);
        var rootFull = Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when a file belongs to a linked folder rather than to the vault.</summary>
    public bool IsLinkedFile(string fullPath)
    {
        var full = Path.GetFullPath(fullPath);
        foreach (var root in LinkedRoots.Values)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>The scope a vault-relative path belongs to: a workspace, a linked folder, or none.</summary>
    public static string WorkspaceOf(string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Length <= 2) return "";
        if (parts[0] == "workspaces" || parts[0] == LinkedPrefix) return parts[1];
        return "";
    }

    public static bool IsArchivedPath(string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        return parts.Length > 3 && parts[0] == "workspaces" && parts[2] == "archive";
    }

    /// <summary>Every Markdown file that is authoritative node content.</summary>
    public IEnumerable<string> EnumerateNodeFiles()
    {
        var roots = new List<string> { Workspaces, GlobalSupertags, Templates, SyncedBlocks };
        roots.AddRange(LinkedRoots.Values);
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
        foreach (var variable in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer"})
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

    /// <summary>Reads the linked folders from linked-folders.json.</summary>
    public void LoadLinkedFolders()
    {
        LinkedRoots.Clear();
        var file = Path.Combine(Root, LinkedFoldersFile);
        if (!File.Exists(file)) return;
        try
        {
            using var doc = JsonDocument.Parse(AtomicFile.ReadAllText(file));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            if (!doc.RootElement.TryGetProperty("folders", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            foreach (var item in arr.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                var path = item.TryGetProperty("path", out var p) ? p.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                string clean;
                try { clean = ValidateWorkspaceName(name!); }
                catch (ArgumentException) { continue; }
                // A workspace of the same name would make the two indistinguishable.
                if (Directory.Exists(Path.Combine(Workspaces, clean))) continue;
                var full = Path.GetFullPath(path!);
                if (IsInside(full) || IsInsideVault(full)) continue;
                // Two linked folders over the same files would index them twice.
                if (LinkedRoots.Values.Any(o => string.Equals(Path.GetFullPath(o), full, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(Path.GetFullPath(o).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
                LinkedRoots[clean] = full;
            }
        }
        catch (JsonException) { /* ignore a damaged file */ }
    }

    /// <summary>The linked folders as they are written to linked-folders.json.</summary>
    public JsonNode LinkedFoldersNode()
    {
        var folders = new JsonArray();
        foreach (var (name, path) in LinkedRoots.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            folders.Add(new JsonObject { ["name"] = name, ["path"] = path });
        return new JsonObject { ["folders"] = folders };
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    private static partial Regex WorkspaceName();

    [GeneratedRegex("_+")]
    private static partial Regex Underscores();

    [GeneratedRegex("^(con|prn|aux|nul|com[0-9]|lpt[0-9])$")]
    private static partial Regex ReservedDeviceName();
}
