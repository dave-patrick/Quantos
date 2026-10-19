using System.Globalization;
using Quantos.Core.Markdown;

namespace Quantos.Core;

public static class NodeKeys
{
    public const string Id = "id", Title = "title", Type = "type", Workspace = "workspace", Tags = "tags",
        Aliases = "aliases", Created = "created", Modified = "modified", Schema = "schema", Archived = "archived",
        Locked = "locked";

    public static readonly HashSet<string> Reserved = [Id, Title, Type, Workspace, Tags, Aliases, Created, Modified, Schema, Archived, Locked];
}

public static class NodeTypes
{
    public const string Note = "note", Daily = "daily", Task = "task", Person = "person", Project = "project",
        Event = "event", Template = "template", Supertag = "supertag", SyncedBlock = "synced_block";
}

/// <summary>A node as read from disk. Immutable snapshot; <see cref="Content"/> is the exact file text.</summary>
public sealed record Node
{
    public const int CurrentSchema = 1;

    public required string Id { get; init; }
    public required string Path { get; init; }           // vault-relative, forward slashes
    public required string Content { get; init; }
    public required string Hash { get; init; }
    public string Title { get; init; } = "";
    public string Type { get; init; } = NodeTypes.Note;
    public string Workspace { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public string? Created { get; init; }
    public string? Modified { get; init; }
    public int Schema { get; init; } = CurrentSchema;
    public bool Archived { get; init; }
    /// <summary>Locked by the person (Release 1.1 read-only mode): nobody edits it until they unlock it.</summary>
    public bool Locked { get; init; }
    public IReadOnlyDictionary<string, object?> Properties { get; init; } = new Dictionary<string, object?>();
    public string Body { get; init; } = "";
    public string? YamlError { get; init; }

    /// <summary>Files with a newer schema than this build understands, or broken YAML, are read-only.</summary>
    public bool ReadOnly => Schema > CurrentSchema || YamlError is not null;

    public static Node FromText(string relativePath, string content, string? hash = null)
    {
        var doc = FrontmatterDocument.Parse(content);
        var values = doc.Values();
        if (doc.YamlError is not null)
        {
            // A mistake elsewhere in the frontmatter (often an edit made outside Quantos) mustn't cost the
            // note its identity: recover each entry on its own, so the note stays listed, linked and
            // openable (read-only, in source mode) until the YAML is fixed.
            values = new Dictionary<string, object?>();
            foreach (var key in doc.Keys) if (doc.Get(key) is { } value) values[key] = value;
        }
        string? Str(string key) => values.TryGetValue(key, out var v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        var props = values.Where(kv => !NodeKeys.Reserved.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        _ = int.TryParse(Str(NodeKeys.Schema), out var schema);
        return new Node
        {
            Id = Str(NodeKeys.Id) ?? "",
            Path = relativePath.Replace('\\', '/'),
            Content = content,
            Hash = hash ?? Hashing.Sha256(content),
            Title = Str(NodeKeys.Title) ?? System.IO.Path.GetFileNameWithoutExtension(relativePath),
            Type = Str(NodeKeys.Type) ?? NodeTypes.Note,
            Workspace = Str(NodeKeys.Workspace) ?? "",
            Tags = doc.GetStringList(NodeKeys.Tags).Select(t => t.TrimStart('#')).ToList(),
            Aliases = doc.GetStringList(NodeKeys.Aliases),
            Created = Str(NodeKeys.Created),
            Modified = Str(NodeKeys.Modified),
            Schema = schema > 0 ? schema : CurrentSchema,
            Archived = string.Equals(Str(NodeKeys.Archived), "true", StringComparison.OrdinalIgnoreCase),
            Locked = string.Equals(Str(NodeKeys.Locked), "true", StringComparison.OrdinalIgnoreCase),
            Properties = props,
            Body = doc.Body,
            YamlError = doc.YamlError
        };
    }
}
