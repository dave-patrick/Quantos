using System.Text;
using System.Text.Json;
using Quantos.Core.Cache;
using Quantos.Core.Markdown;
using Quantos.Core.Security;
using Quantos.Core.Storage;

namespace Quantos.Core;

public enum SaveStatus { Saved, Unchanged, Conflict, NotFound, ReadOnly, Invalid }

public sealed record AmbiguousLink(string Text, IReadOnlyList<NodeSummary> Candidates);

public sealed record SaveResult(SaveStatus Status, Node? Node, string? DiskContent = null, string? DiskHash = null,
    string? Message = null, IReadOnlyList<AmbiguousLink>? Ambiguous = null);

public sealed record CreateNodeRequest
{
    public required string Title { get; init; }
    public string Type { get; init; } = NodeTypes.Note;
    public string Workspace { get; init; } = Vault.DefaultWorkspace;
    public string Body { get; init; } = "";
    public IReadOnlyList<string>? Tags { get; init; }
    public IReadOnlyDictionary<string, object?>? Properties { get; init; }
    public DateOnly? Date { get; init; }
    /// <summary>Optional destination folder (vault-relative) inside the workspace.</summary>
    public string? Folder { get; init; }
}

public sealed class QuantosException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>
/// The single write path (BUILD-PLAN section 6). Every change to vault content - editor saves,
/// property edits, renames, moves, trash, restore, rollback, capture, CLI, agents - goes through
/// here: permission check, conflict check, history, atomic write, index update, notification.
/// </summary>
public sealed class WriteService
{
    private readonly Lock _gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public WriteService(Vault vault, CacheIndex cache, PermissionService permissions, ChangeHub changes, Clock clock)
    {
        Vault = vault;
        Cache = cache;
        Permissions = permissions;
        Changes = changes;
        Clock = clock;
        History = new HistoryStore(vault, clock);
        TrashBin = new TrashStore(vault, clock);
        Journal = new Journal(vault) { BeforeChange = SnapshotBefore };
    }

    public Vault Vault { get; }
    public CacheIndex Cache { get; }
    public PermissionService Permissions { get; }
    public ChangeHub Changes { get; }
    public Clock Clock { get; }
    public HistoryStore History { get; }
    public TrashStore TrashBin { get; }
    public Journal Journal { get; }

    /// <summary>Set by the task service so inline task lines bind to task nodes and stay in sync.</summary>
    public Tasks.ITaskHooks? TaskHooks { get; set; }

    /// <summary>Raised after a node's body was written (synced blocks propagate from here).</summary>
    public event Action<Node, Actor>? BodyWritten;

    /// <summary>Raised after tags are added to a node (linked templates apply automatically).</summary>
    public event Action<Node, IReadOnlyList<string>, Actor>? TagsAdded;

    private void RaiseTagsAdded(Node node, IEnumerable<string> before, Actor actor)
    {
        var added = node.Tags.Where(t => !before.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        if (added.Count > 0) TagsAdded?.Invoke(node, added, actor);
    }

    /// <summary>Runs <paramref name="action"/> while holding the vault write lock (used by reconciliation and the watcher).</summary>
    public T Exclusive<T>(Func<T> action) { lock (_gate) return action(); }

    // ---- reads ------------------------------------------------------------------------------

    public Node? Get(string id)
    {
        var rel = Cache.PathOf(id);
        if (rel is null) return null;
        var abs = Vault.Absolute(rel);
        return File.Exists(abs) ? Node.FromText(rel, AtomicFile.ReadAllText(abs)) : null;
    }

    public Node Require(string id) => Get(id) ?? throw new QuantosException($"Node {id} was not found.", 404);

    public Node? GetByPath(string relative)
    {
        var abs = Vault.Absolute(relative);
        return File.Exists(abs) ? Node.FromText(Vault.Relative(abs), AtomicFile.ReadAllText(abs)) : null;
    }

    // ---- create / save ----------------------------------------------------------------------

    public Node Create(CreateNodeRequest request, Actor actor)
    {
        // Same rule as renaming: no stray spaces around a title, and never an empty one.
        request = request with { Title = string.IsNullOrWhiteSpace(request.Title) ? "Untitled" : request.Title.Trim() };
        Permissions.Demand(actor, Permission.CreateEdit, $"Create {request.Type} '{request.Title}'");
        lock (_gate)
        {
            var workspace = request.Type is NodeTypes.Template or NodeTypes.SyncedBlock || (request.Type == NodeTypes.Supertag && string.IsNullOrEmpty(request.Workspace))
                ? "" : Vault.ValidateWorkspaceName(request.Workspace);
            if (workspace.Length > 0) Vault.EnsureWorkspace(workspace);

            string abs;
            if (request.Type == NodeTypes.Daily)
            {
                var date = request.Date ?? Clock.Today;
                abs = Vault.DailyPath(workspace, date);
                if (File.Exists(abs)) return Node.FromText(Vault.Relative(abs), AtomicFile.ReadAllText(abs));
            }
            else
            {
                var folder = request.Folder is { Length: > 0 } f ? Vault.Absolute(f) : Vault.FolderFor(workspace, request.Type);
                if (workspace.Length > 0 && !folder.StartsWith(Vault.WorkspaceDir(workspace), StringComparison.OrdinalIgnoreCase) && request.Folder is not null)
                    throw new QuantosException("The folder must be inside the workspace.");
                abs = Vault.UniquePath(folder, Vault.Slug(request.Title));
            }

            var id = Ulid.New();
            var now = Clock.Stamp();
            var doc = FrontmatterDocument.Create();
            doc.Set(NodeKeys.Id, id);
            doc.Set(NodeKeys.Title, request.Title);
            doc.Set(NodeKeys.Type, request.Type);
            if (workspace.Length > 0) doc.Set(NodeKeys.Workspace, workspace);
            if (request.Tags is { Count: > 0 }) doc.Set(NodeKeys.Tags, request.Tags.Select(t => t.TrimStart('#')).ToList());
            doc.Set(NodeKeys.Created, now);
            doc.Set(NodeKeys.Modified, now);
            doc.Set(NodeKeys.Schema, Node.CurrentSchema);
            if (request.Type == NodeTypes.Daily) doc.Set("date", (request.Date ?? Clock.Today).ToString("yyyy-MM-dd"));
            foreach (var (key, value) in request.Properties ?? new Dictionary<string, object?>())
            {
                if (NodeKeys.Reserved.Contains(key)) continue;
                doc.Set(key, value);
            }
            var body = ResolveLinks(request.Body, workspace).Body;
            doc.SetBody(TaskHooks?.BindInlineTasks(doc, body, actor) ?? body);

            var node = WriteFile(abs, id, null, doc.ToString());
            Changes.Publish(new ChangeEvent(ChangeKind.Created, id, node.Path, Hash: node.Hash, Origin: actor.ToString()));
            ResolveWaitingLinks(node, actor);
            RaiseTagsAdded(node, [], actor);
            return Get(id) ?? node;
        }
    }

    /// <summary>Links typed before their target existed resolve as soon as it is created (unless still ambiguous).</summary>
    private void ResolveWaitingLinks(Node created, Actor actor)
    {
        foreach (var title in created.Aliases.Prepend(created.Title))
        foreach (var referrer in Cache.UnresolvedReferrers(title).Where(r => r != created.Id))
        {
            var note = Get(referrer);
            if (note is null || note.ReadOnly) continue;
            var resolved = ResolveLinks(note.Body, note.Workspace).Body;
            if (resolved != note.Body) UpdateFields(referrer, null, null, null, Actor.System with { Instance = actor.Instance }, resolved);
        }
    }

    public Node GetOrCreateDaily(string workspace, DateOnly date, Actor actor)
    {
        var abs = Vault.DailyPath(workspace, date);
        if (File.Exists(abs))
        {
            var existing = Node.FromText(Vault.Relative(abs), AtomicFile.ReadAllText(abs));
            if (Ulid.IsValid(existing.Id)) return existing;
        }
        return Create(new CreateNodeRequest { Title = date.ToString("yyyy-MM-dd"), Type = NodeTypes.Daily, Workspace = workspace, Date = date }, actor);
    }

    /// <summary>Saves full file text. Refused as a conflict when the file on disk isn't the version the caller last saw.</summary>
    public SaveResult Save(string id, string content, string? baseHash, Actor actor)
    {
        Permissions.Demand(actor, Permission.CreateEdit, $"Edit {id}");
        lock (_gate)
        {
            var (abs, disk) = ReadCurrent(id);
            if (abs is null || disk is null) return new SaveResult(SaveStatus.NotFound, null, Message: "The node no longer exists.");
            var diskHash = Hashing.Sha256(disk);
            if (baseHash is not null && baseHash != diskHash)
            {
                Changes.Publish(new ChangeEvent(ChangeKind.Conflict, id, Vault.Relative(abs), Hash: diskHash, Origin: actor.ToString()));
                return new SaveResult(SaveStatus.Conflict, null, disk, diskHash, "The file changed on disk since you opened it.");
            }
            var current = Node.FromText(Vault.Relative(abs), disk, diskHash);
            if (current.Schema > Node.CurrentSchema)
                return new SaveResult(SaveStatus.ReadOnly, current, Message: $"This file uses schema {current.Schema}, newer than this version of Quantos understands.");

            var doc = FrontmatterDocument.Parse(content);
            if (doc.YamlError is not null)
                return new SaveResult(SaveStatus.Invalid, null, Message: "The frontmatter isn't valid YAML: " + doc.YamlError);
            if (doc.GetString(NodeKeys.Id) != id) doc.Set(NodeKeys.Id, id);

            var (body, ambiguous) = ResolveLinks(doc.Body, current.Workspace);
            doc.SetBody(TaskHooks?.BindInlineTasks(doc, body, actor) ?? body);
            var candidate = doc.ToString();
            if (candidate == disk) return new SaveResult(SaveStatus.Unchanged, current, Ambiguous: ambiguous);
            doc.Set(NodeKeys.Modified, Clock.Stamp());

            var node = WriteFile(abs, id, disk, doc.ToString());
            Changes.Publish(new ChangeEvent(ChangeKind.Updated, id, node.Path, Hash: node.Hash, Origin: actor.ToString()));
            TaskHooks?.AfterNoteWritten(node, actor);
            BodyWritten?.Invoke(node, actor);
            return new SaveResult(SaveStatus.Saved, Get(id) ?? node, Ambiguous: ambiguous);
        }
    }

    /// <summary>Sets or removes frontmatter properties and optionally replaces the body.</summary>
    public SaveResult UpdateFields(string id, string? baseHash, IReadOnlyDictionary<string, object?>? set, IEnumerable<string>? remove, Actor actor, string? body = null, bool allowReserved = false)
    {
        Permissions.Demand(actor, Permission.CreateEdit, $"Edit properties of {id}");
        lock (_gate)
        {
            var (abs, disk) = ReadCurrent(id);
            if (abs is null || disk is null) return new SaveResult(SaveStatus.NotFound, null);
            var diskHash = Hashing.Sha256(disk);
            if (baseHash is not null && baseHash != diskHash)
                return new SaveResult(SaveStatus.Conflict, null, disk, diskHash, "The file changed on disk since you opened it.");
            var doc = FrontmatterDocument.Parse(disk);
            if (doc.YamlError is not null) return new SaveResult(SaveStatus.Invalid, null, Message: "The frontmatter isn't valid YAML; edit it in source mode.");
            var protectedKeys = allowReserved ? new HashSet<string> { NodeKeys.Id } : new HashSet<string> { NodeKeys.Id, NodeKeys.Created, NodeKeys.Schema, NodeKeys.Workspace, NodeKeys.Archived };
            foreach (var (key, value) in set ?? new Dictionary<string, object?>())
            {
                if (protectedKeys.Contains(key)) throw new QuantosException($"'{key}' can't be changed this way.");
                if (key == NodeKeys.Title && !allowReserved) throw new QuantosException("Use rename to change the title.");
                doc.Set(key, value);
            }
            foreach (var key in remove ?? [])
            {
                if (protectedKeys.Contains(key) || key == NodeKeys.Title) throw new QuantosException($"'{key}' can't be removed.");
                doc.Remove(key);
            }
            if (body is not null)
            {
                var workspace = doc.GetString(NodeKeys.Workspace) ?? "";
                var resolved = ResolveLinks(body, workspace).Body;
                doc.SetBody(TaskHooks?.BindInlineTasks(doc, resolved, actor) ?? resolved);
            }
            if (doc.ToString() == disk) return new SaveResult(SaveStatus.Unchanged, Node.FromText(Vault.Relative(abs), disk, diskHash));
            doc.Set(NodeKeys.Modified, Clock.Stamp());
            var node = WriteFile(abs, id, disk, doc.ToString());
            Changes.Publish(new ChangeEvent(ChangeKind.Updated, id, node.Path, Hash: node.Hash, Origin: actor.ToString()));
            if (body is not null) { TaskHooks?.AfterNoteWritten(node, actor); BodyWritten?.Invoke(node, actor); }
            if (set?.ContainsKey(NodeKeys.Tags) == true) RaiseTagsAdded(node, Node.FromText(node.Path, disk).Tags, actor);
            return new SaveResult(SaveStatus.Saved, Get(id) ?? node);
        }
    }

    // ---- multi-file operations (journaled) --------------------------------------------------

    public Node Rename(string id, string newTitle, Actor actor)
    {
        newTitle = newTitle.Trim();
        if (newTitle.Length == 0) throw new QuantosException("The title can't be empty.");
        Permissions.Demand(actor, Permission.CreateEdit, $"Rename {id} to '{newTitle}'");
        lock (_gate)
        {
            var node = Require(id);
            if (node.Title == newTitle) return node;
            var abs = Vault.Absolute(node.Path);
            var doc = FrontmatterDocument.Parse(node.Content);
            doc.Set(NodeKeys.Title, newTitle);
            doc.Set(NodeKeys.Modified, Clock.Stamp());

            var op = new JournalOperation { Description = $"Rename '{node.Title}' to '{newTitle}'" };
            op.Steps.Add(JournalStep.Write(node.Path, node.Content, doc.ToString()));
            var newAbs = abs;
            if (node.Type != NodeTypes.Daily)
            {
                newAbs = Vault.UniquePath(Path.GetDirectoryName(abs)!, Vault.Slug(newTitle), except: abs);
                if (!Vault.PathEquals(newAbs, abs)) op.Steps.Add(JournalStep.Move(node.Path, Vault.Relative(newAbs)));
            }
            var touched = AddLinkRewrites(op, id, node.Title, newTitle);
            Journal.Execute(op);

            if (!Vault.PathEquals(newAbs, abs)) Cache.Remove(node.Path);
            var renamed = AfterJournal(newAbs);
            foreach (var path in touched) AfterJournal(Vault.Absolute(path));
            Changes.Publish(new ChangeEvent(Vault.PathEquals(newAbs, abs) ? ChangeKind.Updated : ChangeKind.Moved, id, renamed.Path, node.Path, renamed.Hash, actor.ToString()));
            foreach (var path in touched) PublishUpdated(path, actor);
            return renamed;
        }
    }

    public Node MoveToWorkspace(string id, string workspace, Actor actor)
    {
        Vault.ValidateWorkspaceName(workspace);
        Permissions.Demand(actor, Permission.MoveArchive, $"Move {id} to workspace {workspace}");
        lock (_gate)
        {
            var node = Require(id);
            if (node.Workspace == workspace) return node;
            Vault.EnsureWorkspace(workspace);
            var abs = Vault.Absolute(node.Path);
            var folder = node.Archived ? Path.Combine(Vault.WorkspaceDir(workspace), "archive") : Vault.FolderFor(workspace, node.Type, DailyDate(node));
            var newAbs = node.Type == NodeTypes.Daily && !node.Archived
                ? Vault.DailyPath(workspace, DailyDate(node) ?? Clock.Today)
                : Vault.UniquePath(folder, Path.GetFileNameWithoutExtension(abs));
            if (File.Exists(newAbs)) throw new QuantosException($"{Vault.Relative(newAbs)} already exists in the destination workspace.", 409);
            return MoveWithFields(node, newAbs, d => d.Set(NodeKeys.Workspace, workspace), $"Move '{node.Title}' to {workspace}", actor, ChangeKind.Moved);
        }
    }

    /// <summary>Moves a node to another folder inside its workspace (file tree drag and drop).
    /// A name collision is refused (409) unless <paramref name="allowRename"/> picks a numbered name.</summary>
    public Node MoveToFolder(string id, string folderRelative, Actor actor, bool allowRename = false)
    {
        Permissions.Demand(actor, Permission.MoveArchive, $"Move {id} to {folderRelative}");
        lock (_gate)
        {
            var node = Require(id);
            var folder = Vault.Absolute(folderRelative);
            if (node.Workspace.Length > 0 && !folder.StartsWith(Vault.WorkspaceDir(node.Workspace), StringComparison.OrdinalIgnoreCase))
                throw new QuantosException("Use a workspace move to move between workspaces.");
            var abs = Vault.Absolute(node.Path);
            if (Vault.PathEquals(Path.GetDirectoryName(abs), folder)) return node;
            if (!allowRename && File.Exists(Path.Combine(folder, Path.GetFileName(abs))))
                throw new QuantosException($"A file named {Path.GetFileName(abs)} already exists in {folderRelative}.", 409);
            var newAbs = Vault.UniquePath(folder, Path.GetFileNameWithoutExtension(abs));
            return MoveWithFields(node, newAbs, _ => { }, $"Move '{node.Title}' to {folderRelative}", actor, ChangeKind.Moved);
        }
    }

    public Node SetArchived(string id, bool archived, Actor actor)
    {
        Permissions.Demand(actor, Permission.MoveArchive, $"{(archived ? "Archive" : "Unarchive")} {id}");
        lock (_gate)
        {
            var node = Require(id);
            if (node.Archived == archived && Vault.IsArchivedPath(node.Path) == archived) return node;
            var abs = Vault.Absolute(node.Path);
            var ws = node.Workspace.Length > 0 ? node.Workspace : Vault.WorkspaceOf(node.Path);
            string newAbs;
            if (archived && ws.Length > 0)
                newAbs = Vault.UniquePath(Path.Combine(Vault.WorkspaceDir(ws), "archive"), Path.GetFileNameWithoutExtension(abs));
            else if (!archived && node.Type == NodeTypes.Daily && DailyDate(node) is { } date)
                newAbs = Vault.DailyPath(ws, date);
            else if (!archived)
                newAbs = Vault.UniquePath(Vault.FolderFor(ws, node.Type), Path.GetFileNameWithoutExtension(abs));
            else newAbs = abs;
            if (!Vault.PathEquals(newAbs, abs) && File.Exists(newAbs)) throw new QuantosException($"{Vault.Relative(newAbs)} already exists.", 409);
            return MoveWithFields(node, newAbs, d =>
            {
                if (archived) d.Set(NodeKeys.Archived, true); else d.Remove(NodeKeys.Archived);
            }, $"{(archived ? "Archive" : "Unarchive")} '{node.Title}'", actor, archived ? ChangeKind.Archived : ChangeKind.Unarchived);
        }
    }

    private Node MoveWithFields(Node node, string newAbs, Action<FrontmatterDocument> edit, string description, Actor actor, ChangeKind kind)
    {
        var abs = Vault.Absolute(node.Path);
        var doc = FrontmatterDocument.Parse(node.Content);
        edit(doc);
        var op = new JournalOperation { Description = description };
        if (doc.ToString() != node.Content)
        {
            doc.Set(NodeKeys.Modified, Clock.Stamp());
            op.Steps.Add(JournalStep.Write(node.Path, node.Content, doc.ToString()));
        }
        if (!Vault.PathEquals(newAbs, abs)) op.Steps.Add(JournalStep.Move(node.Path, Vault.Relative(newAbs)));
        if (op.Steps.Count == 0) return node;
        Journal.Execute(op);
        if (!Vault.PathEquals(newAbs, abs)) Cache.Remove(node.Path);
        var moved = AfterJournal(newAbs);
        Changes.Publish(new ChangeEvent(kind, node.Id, moved.Path, node.Path, moved.Hash, actor.ToString()));
        return moved;
    }

    public TrashEntry Trash(string id, Actor actor)
    {
        Permissions.Demand(actor, Permission.TrashRestore, $"Move {id} to trash");
        lock (_gate)
        {
            var node = Require(id);
            var abs = Vault.Absolute(node.Path);
            History.Record(id, Encoding.UTF8.GetBytes(node.Content), HistoryStore.Before);
            var (entryDir, destination) = TrashBin.Plan(id, abs);
            var entry = new TrashEntry
            {
                EntryId = Path.GetFileName(entryDir), Id = id, Title = node.Title, Type = node.Type, OriginalPath = node.Path,
                FileName = Path.GetFileName(abs), TrashedAt = Clock.Now,
                Referrers = Cache.Referrers(id).Where(r => r != id).ToList()
            };
            var manifestRel = Vault.Relative(Path.Combine(entryDir, "manifest.json"));
            var op = new JournalOperation { Description = $"Trash '{node.Title}'" };
            op.Steps.Add(JournalStep.Write(manifestRel, null, JsonSerializer.Serialize(entry, Json)));
            op.Steps.Add(JournalStep.Move(node.Path, Vault.Relative(destination)));
            Journal.Execute(op);
            Cache.Remove(node.Path);
            Changes.Publish(new ChangeEvent(ChangeKind.Trashed, id, null, node.Path, Origin: actor.ToString(), Data: entry));
            return entry;
        }
    }

    public Node Restore(string entryId, Actor actor)
    {
        Permissions.Demand(actor, Permission.TrashRestore, $"Restore {entryId} from trash");
        lock (_gate)
        {
            var entry = TrashBin.Read(entryId) ?? throw new QuantosException("That item is no longer in the trash.", 404);
            var file = TrashBin.EntryFile(entry);
            if (!File.Exists(file)) throw new QuantosException("The trashed file is missing.", 404);
            if (entry.Type == "attachment") return RestoreAttachment(entry, file, actor);
            if (Cache.PathOf(entry.Id) is { } live) throw new QuantosException($"A node with the same identity already exists at {live}.", 409);

            var target = Vault.Absolute(entry.OriginalPath);
            var ws = Vault.WorkspaceOf(entry.OriginalPath);
            if (ws.Length > 0) Vault.EnsureWorkspace(ws);
            if (File.Exists(target)) target = Vault.UniquePath(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target));
            var manifest = Path.Combine(TrashBin.EntryDir(entryId), "manifest.json");

            var op = new JournalOperation { Description = $"Restore '{entry.Title}'" };
            op.Steps.Add(JournalStep.Move(Vault.Relative(file), Vault.Relative(target)));
            op.Steps.Add(JournalStep.Delete(Vault.Relative(manifest), AtomicFile.ReadAllText(manifest)));
            Journal.Execute(op);
            TryDeleteEmptyDir(TrashBin.EntryDir(entryId));

            var node = AfterJournal(target);
            History.Record(node.Id, Encoding.UTF8.GetBytes(node.Content), HistoryStore.Restore);
            Changes.Publish(new ChangeEvent(ChangeKind.Restored, node.Id, node.Path, entry.OriginalPath, node.Hash, actor.ToString(), entry));
            return node;
        }
    }

    private Node RestoreAttachment(TrashEntry entry, string file, Actor actor)
    {
        var target = Vault.Absolute(entry.OriginalPath);
        var manifest = Path.Combine(TrashBin.EntryDir(entry.EntryId), "manifest.json");
        var op = new JournalOperation { Description = $"Restore attachment {entry.OriginalPath}" };
        if (!File.Exists(target)) op.Steps.Add(JournalStep.Move(Vault.Relative(file), entry.OriginalPath));
        op.Steps.Add(JournalStep.Delete(Vault.Relative(manifest), AtomicFile.ReadAllText(manifest)));
        Journal.Execute(op);
        if (File.Exists(file)) File.Delete(file); // identical content already back in place
        TryDeleteEmptyDir(TrashBin.EntryDir(entry.EntryId));
        Changes.Publish(new ChangeEvent(ChangeKind.Restored, null, entry.OriginalPath, Origin: actor.ToString(), Data: entry));
        return new Node { Id = entry.Id, Path = entry.OriginalPath, Content = "", Hash = "", Title = entry.Title, Type = "attachment" };
    }

    public void DeletePermanently(string entryId, Actor actor, string? approvalId)
    {
        var entry = TrashBin.Read(entryId) ?? throw new QuantosException("That item is no longer in the trash.", 404);
        Permissions.Demand(actor, Permission.PermanentDelete, $"Permanently delete '{entry.Title}' from the trash", approvalId);
        lock (_gate)
        {
            Directory.Delete(TrashBin.EntryDir(entryId), recursive: true);
            Changes.Publish(new ChangeEvent(ChangeKind.Deleted, entry.Id, null, entry.OriginalPath, Origin: actor.ToString(), Data: entry));
        }
    }

    public Node Rollback(string id, string version, Actor actor)
    {
        Permissions.Demand(actor, Permission.CreateEdit, $"Roll back {id} to {version}");
        lock (_gate)
        {
            var content = History.Read(id, version) ?? throw new QuantosException("That version doesn't exist.", 404);
            var (abs, disk) = ReadCurrent(id);
            if (abs is null || disk is null) throw new QuantosException("The node no longer exists; restore it from the trash first.", 404);
            var doc = FrontmatterDocument.Parse(content);
            if (doc.YamlError is null && doc.GetString(NodeKeys.Id) != id) doc.Set(NodeKeys.Id, id);
            var node = WriteFile(abs, id, disk, doc.ToString(), HistoryStore.Restore);
            Changes.Publish(new ChangeEvent(ChangeKind.Updated, id, node.Path, Hash: node.Hash, Origin: actor.ToString()));
            return node;
        }
    }

    /// <summary>Keeps a version in history without changing the file (the losing side of a conflict).</summary>
    public string? PreserveCopy(string id, string content, Actor actor)
    {
        Permissions.Demand(actor, Permission.CreateEdit, $"Keep a copy of {id}");
        if (Cache.PathOf(id) is null && History.List(id).Count == 0) throw new QuantosException("Unknown node.", 404);
        return History.Record(id, Encoding.UTF8.GetBytes(content), "conflict");
    }

    /// <summary>Appends text to a node's body, merging on the server so open editors aren't overwritten.</summary>
    public Node AppendToBody(string id, string text, Actor actor, string? underHeading = null)
    {
        Permissions.Demand(actor, Permission.CreateEdit, $"Append to {id}");
        lock (_gate)
        {
            var (abs, disk) = ReadCurrent(id);
            if (abs is null || disk is null) throw new QuantosException("The node no longer exists.", 404);
            var doc = FrontmatterDocument.Parse(disk);
            var body = doc.Body;
            var nl = doc.Newline;
            if (underHeading is not null && !body.Contains(underHeading))
                body = body.TrimEnd() + (body.Trim().Length > 0 ? nl + nl : "") + underHeading + nl;
            else if (body.Length > 0 && !body.EndsWith('\n')) body += nl;
            body += text.Replace("\r\n", "\n").Replace("\n", nl) + nl;
            var (resolved, _) = ResolveLinks(body, doc.GetString(NodeKeys.Workspace) ?? "");
            doc.SetBody(TaskHooks?.BindInlineTasks(doc, resolved, actor) ?? resolved);
            doc.Set(NodeKeys.Modified, Clock.Stamp());
            var node = WriteFile(abs, id, disk, doc.ToString());
            Changes.Publish(new ChangeEvent(ChangeKind.Updated, id, node.Path, Hash: node.Hash, Origin: actor.ToString()));
            return node;
        }
    }

    /// <summary>
    /// Extracts text from a node into a new note and leaves a link (or embed) in its place
    /// (BUILD-PLAN 8A.15). Both files change in one journaled operation.
    /// </summary>
    public (Node Source, Node Extracted) Extract(string id, string text, string title, bool embed, Actor actor)
    {
        Permissions.Demand(actor, Permission.CreateEdit, $"Extract from {id}");
        title = title.Trim();
        if (title.Length == 0 || text.Trim().Length == 0) throw new QuantosException("Choose some text and a title.");
        lock (_gate)
        {
            var source = Require(id);
            var doc = FrontmatterDocument.Parse(source.Content);
            var at = doc.Body.IndexOf(text, StringComparison.Ordinal);
            if (at < 0) throw new QuantosException("That text isn't in the note any more; it may have changed.", 409);
            var workspace = source.Workspace.Length > 0 ? source.Workspace : Vault.DefaultWorkspace;
            var newId = Ulid.New();
            var now = Clock.Stamp();
            var created = FrontmatterDocument.Create();
            created.Set(NodeKeys.Id, newId);
            created.Set(NodeKeys.Title, title);
            created.Set(NodeKeys.Type, NodeTypes.Note);
            created.Set(NodeKeys.Workspace, workspace);
            created.Set(NodeKeys.Created, now);
            created.Set(NodeKeys.Modified, now);
            created.Set(NodeKeys.Schema, Node.CurrentSchema);
            created.Set("extracted_from", $"[[{source.Id}|{source.Title}]]");
            created.SetBody(text.Trim() + "\n");
            var newAbs = Vault.UniquePath(Vault.FolderFor(workspace, NodeTypes.Note), Vault.Slug(title));
            var link = (embed ? "!" : "") + $"[[{newId}|{title}]]";
            doc.SetBody(doc.Body[..at] + link + doc.Body[(at + text.Length)..]);
            doc.Set(NodeKeys.Modified, now);

            var op = new JournalOperation { Description = $"Extract '{title}' from '{source.Title}'" };
            op.Steps.Add(JournalStep.Write(Vault.Relative(newAbs), null, created.ToString()));
            op.Steps.Add(JournalStep.Write(source.Path, source.Content, doc.ToString()));
            Journal.Execute(op);
            var extracted = AfterJournal(newAbs);
            var updated = AfterJournal(Vault.Absolute(source.Path));
            Changes.Publish(new ChangeEvent(ChangeKind.Created, extracted.Id, extracted.Path, Hash: extracted.Hash, Origin: actor.ToString()));
            Changes.Publish(new ChangeEvent(ChangeKind.Updated, updated.Id, updated.Path, Hash: updated.Hash, Origin: actor.ToString()));
            return (updated, extracted);
        }
    }

    /// <summary>
    /// Merges <paramref name="otherId"/> into <paramref name="id"/>: bodies are joined, missing
    /// properties and tags copied, links to the other node re-pointed, and the other node goes to
    /// the trash (restorable). One journaled operation.
    /// </summary>
    public Node Merge(string id, string otherId, Actor actor)
    {
        if (id == otherId) throw new QuantosException("A note can't be merged into itself.");
        Permissions.Demand(actor, Permission.CreateEdit, $"Merge {otherId} into {id}");
        Permissions.Demand(actor, Permission.TrashRestore, $"Merge {otherId} into {id}");
        lock (_gate)
        {
            var target = Require(id);
            var other = Require(otherId);
            var doc = FrontmatterDocument.Parse(target.Content);
            foreach (var (key, value) in other.Properties)
                if (!target.Properties.ContainsKey(key)) doc.Set(key, value);
            var tags = target.Tags.Union(other.Tags, StringComparer.OrdinalIgnoreCase).ToList();
            if (tags.Count != target.Tags.Count) doc.Set(NodeKeys.Tags, tags);
            var aliases = target.Aliases.Union([other.Title, .. other.Aliases], StringComparer.OrdinalIgnoreCase).Where(a => !a.Equals(target.Title, StringComparison.OrdinalIgnoreCase)).ToList();
            doc.Set(NodeKeys.Aliases, aliases);
            doc.SetBody(target.Body.TrimEnd() + "\n\n## " + other.Title + "\n\n" + other.Body.Trim() + "\n");
            doc.Set(NodeKeys.Modified, Clock.Stamp());

            var op = new JournalOperation { Description = $"Merge '{other.Title}' into '{target.Title}'" };
            op.Steps.Add(JournalStep.Write(target.Path, target.Content, doc.ToString()));
            var touched = new List<string>();
            foreach (var referrer in Cache.Referrers(otherId).Where(r => r != id && r != otherId))
            {
                var refNode = Get(referrer);
                if (refNode is null || refNode.ReadOnly) continue;
                var refDoc = FrontmatterDocument.Parse(refNode.Content);
                var body = Links.Rewrite(refDoc.Body, l => l.Target == otherId ? Links.Format(l.Kind, id, l.Fragment, l.Display ?? other.Title) : null);
                if (body == refDoc.Body) continue;
                refDoc.SetBody(body);
                op.Steps.Add(JournalStep.Write(refNode.Path, refNode.Content, refDoc.ToString()));
                touched.Add(refNode.Path);
            }
            var otherAbs = Vault.Absolute(other.Path);
            History.Record(otherId, Encoding.UTF8.GetBytes(other.Content), HistoryStore.Before);
            var (entryDir, destination) = TrashBin.Plan(otherId, otherAbs);
            var entry = new TrashEntry
            {
                EntryId = Path.GetFileName(entryDir), Id = otherId, Title = other.Title, Type = other.Type, OriginalPath = other.Path,
                FileName = Path.GetFileName(otherAbs), TrashedAt = Clock.Now, Referrers = []
            };
            op.Steps.Add(JournalStep.Write(Vault.Relative(Path.Combine(entryDir, "manifest.json")), null, JsonSerializer.Serialize(entry, Json)));
            op.Steps.Add(JournalStep.Move(other.Path, Vault.Relative(destination)));
            Journal.Execute(op);

            Cache.Remove(other.Path);
            var merged = AfterJournal(Vault.Absolute(target.Path));
            foreach (var path in touched) { AfterJournal(Vault.Absolute(path)); PublishUpdated(path, actor); }
            Changes.Publish(new ChangeEvent(ChangeKind.Updated, id, merged.Path, Hash: merged.Hash, Origin: actor.ToString()));
            Changes.Publish(new ChangeEvent(ChangeKind.Trashed, otherId, null, other.Path, Origin: actor.ToString(), Data: entry));
            return merged;
        }
    }

    /// <summary>Gives a file without a valid or unique id a new identity (STORAGE-SPEC section 2, adoption).</summary>
    public Node Adopt(string absolutePath, string reason)
    {
        lock (_gate)
        {
            var disk = AtomicFile.ReadAllText(absolutePath);
            var doc = FrontmatterDocument.Parse(disk);
            if (doc.YamlError is not null)
                throw new QuantosException($"{Vault.Relative(absolutePath)} has invalid frontmatter and can't be adopted.");
            var id = Ulid.New();
            doc.Set(NodeKeys.Id, id);
            if (!doc.ContainsKey(NodeKeys.Title)) doc.Set(NodeKeys.Title, Path.GetFileNameWithoutExtension(absolutePath));
            if (!doc.ContainsKey(NodeKeys.Schema)) doc.Set(NodeKeys.Schema, Node.CurrentSchema);
            var ws = Vault.WorkspaceOf(Vault.Relative(absolutePath));
            if (!doc.ContainsKey(NodeKeys.Workspace) && ws.Length > 0) doc.Set(NodeKeys.Workspace, ws);
            if (!doc.ContainsKey(NodeKeys.Created)) doc.Set(NodeKeys.Created, new DateTimeOffset(File.GetCreationTime(absolutePath)).ToString("yyyy-MM-ddTHH:mm:sszzz"));
            var node = WriteFile(absolutePath, id, null, doc.ToString(), beforeKind: HistoryStore.External, beforeBytes: Encoding.UTF8.GetBytes(disk));
            Changes.Publish(new ChangeEvent(ChangeKind.Created, id, node.Path, Hash: node.Hash, Origin: "adopt:" + reason));
            return node;
        }
    }

    // ---- link resolution --------------------------------------------------------------------

    /// <summary>Turns typed <c>[[Title]]</c> links into <c>[[id|Title]]</c> where exactly one node matches.</summary>
    public (string Body, IReadOnlyList<AmbiguousLink> Ambiguous) ResolveLinks(string body, string workspace)
    {
        var ambiguous = new List<AmbiguousLink>();
        var rewritten = Links.Rewrite(body, link =>
        {
            if (link.IsResolved) return null;
            var (ws, title) = link.TitleTarget;
            if (title.Length == 0) return null;
            var candidates = Cache.FindByTitle(title, ws ?? (workspace.Length > 0 ? workspace : null));
            if (candidates.Count == 0 && ws is null && workspace.Length > 0) candidates = Cache.FindByTitle(title, null);
            if (candidates.Count == 1) return Links.Format(link.Kind, candidates[0].Id, link.Fragment, link.Display ?? title);
            if (candidates.Count > 1) ambiguous.Add(new AmbiguousLink(link.Target, candidates));
            return null;
        });
        return (rewritten, ambiguous);
    }

    private List<string> AddLinkRewrites(JournalOperation op, string id, string oldTitle, string newTitle)
    {
        var touched = new List<string>();
        foreach (var referrer in Cache.Referrers(id).Where(r => r != id))
        {
            var refNode = Get(referrer);
            if (refNode is null || refNode.ReadOnly) continue;
            var doc = FrontmatterDocument.Parse(refNode.Content);
            string Retitle(string text) => Links.Rewrite(text, link =>
                link.Target == id && link.Display is not null && string.Equals(link.Display.Trim(), oldTitle, StringComparison.OrdinalIgnoreCase)
                    ? Links.Format(link.Kind, id, link.Fragment, newTitle)
                    : null);
            var body = Retitle(doc.Body);
            // Links in properties (owner, attendees) carry the title too.
            var propsChanged = false;
            foreach (var key in doc.Keys.Where(k => !NodeKeys.Reserved.Contains(k)).ToList())
            {
                switch (doc.Get(key))
                {
                    case string s when s.Contains("[[" + id, StringComparison.Ordinal) && Retitle(s) is var t && t != s:
                        doc.Set(key, t); propsChanged = true; break;
                    case List<object?> list when list.OfType<string>().Any(x => x.Contains("[[" + id, StringComparison.Ordinal)):
                        var mapped = list.Select(x => x is string xs ? Retitle(xs) : x).ToList();
                        if (!mapped.SequenceEqual(list)) { doc.Set(key, mapped); propsChanged = true; }
                        break;
                }
            }
            if (body == doc.Body && !propsChanged) continue;
            if (body != doc.Body) doc.SetBody(body);
            doc.Set(NodeKeys.Modified, Clock.Stamp());
            op.Steps.Add(JournalStep.Write(refNode.Path, refNode.Content, doc.ToString()));
            touched.Add(refNode.Path);
        }
        return touched;
    }

    // ---- helpers ----------------------------------------------------------------------------

    /// <summary>Journal hook: keep the version about to be replaced, if history doesn't already have it.</summary>
    private void SnapshotBefore(string abs)
    {
        if (!File.Exists(abs)) return;
        var bytes = AtomicFile.ReadAllBytes(abs);
        var id = Node.FromText(Vault.Relative(abs), Encoding.UTF8.GetString(bytes)).Id;
        if (Ulid.IsValid(id)) History.Record(id, bytes, HistoryStore.Before);
    }

    private (string? Abs, string? Content) ReadCurrent(string id)
    {
        var rel = Cache.PathOf(id);
        if (rel is null) return (null, null);
        var abs = Vault.Absolute(rel);
        return File.Exists(abs) ? (abs, AtomicFile.ReadAllText(abs)) : (null, null);
    }

    private Node WriteFile(string abs, string id, string? disk, string content, string kind = HistoryStore.Save,
        string beforeKind = HistoryStore.Before, byte[]? beforeBytes = null)
    {
        var before = beforeBytes ?? (disk is null ? null : Encoding.UTF8.GetBytes(disk));
        if (before is not null) History.Record(id, before, beforeKind);
        AtomicFile.WriteAllText(abs, content);
        History.Record(id, Encoding.UTF8.GetBytes(content), kind);
        return Index(abs, content);
    }

    private Node AfterJournal(string abs)
    {
        var content = AtomicFile.ReadAllText(abs);
        var node = Index(abs, content);
        History.Record(node.Id, Encoding.UTF8.GetBytes(content), HistoryStore.Save);
        return node;
    }

    private void PublishUpdated(string rel, Actor actor)
    {
        var cached = Cache.GetFile(rel);
        if (cached is not null) Changes.Publish(new ChangeEvent(ChangeKind.Updated, cached.Id, rel, Hash: cached.Hash, Origin: actor.ToString()));
    }

    /// <summary>Updates the derived indexes for one file.</summary>
    public Node Index(string abs, string? content = null)
    {
        content ??= AtomicFile.ReadAllText(abs);
        var info = new FileInfo(abs);
        var node = Node.FromText(Vault.Relative(abs), content);
        Cache.Upsert(node, info.LastWriteTimeUtc.Ticks, info.Length);
        return node;
    }

    private static DateOnly? DailyDate(Node node)
    {
        if (node.Properties.TryGetValue("date", out var d) && DateOnly.TryParse(Convert.ToString(d), out var date)) return date;
        return DateOnly.TryParse(Path.GetFileNameWithoutExtension(node.Path), out var fromName) ? fromName : null;
    }

    private static void TryDeleteEmptyDir(string dir)
    {
        try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
        catch (IOException) { }
    }
}
