using Quantos.Core;

namespace Quantos.Backend.Api;

public sealed record NodeDto(
    string Id, string Path, string Title, string Type, string Workspace, IReadOnlyList<string> Tags, IReadOnlyList<string> Aliases,
    string? Created, string? Modified, bool Archived, bool Locked, bool ReadOnly, string? YamlError, int Schema,
    IReadOnlyDictionary<string, object?> Properties, string Body, string Content, string Hash)
{
    public static NodeDto From(Node n) => new(n.Id, n.Path, n.Title, n.Type, n.Workspace, n.Tags, n.Aliases, n.Created, n.Modified,
        n.Archived, n.Locked, n.ReadOnly, n.YamlError, n.Schema, n.Properties, n.Body, n.Content, n.Hash);
}

public sealed record SaveResultDto(string Status, NodeDto? Node, string? DiskContent, string? DiskHash, string? Message, IReadOnlyList<AmbiguousLink>? Ambiguous)
{
    public static SaveResultDto From(SaveResult r) =>
        new(r.Status.ToString().ToLowerInvariant(), r.Node is null ? null : NodeDto.From(r.Node), r.DiskContent, r.DiskHash, r.Message, r.Ambiguous);
}

public sealed record CreateNodeBody(string Title, string? Type, string? Workspace, string? Body, List<string>? Tags,
    Dictionary<string, System.Text.Json.JsonElement>? Properties, string? Folder, string? Date);

public sealed record SaveBody(string Content, string? BaseHash);

public sealed record PatchBody(string? BaseHash, Dictionary<string, System.Text.Json.JsonElement>? Set, List<string>? Remove, string? Body);

public sealed record RenameBody(string Title);

public sealed record MoveBody(string? Workspace, string? Folder, bool? Rename);

public sealed record ArchiveBody(bool Archived);

public sealed record RollbackBody(string Version);

public sealed record VaultPathBody(string Path);

public sealed record WorkspaceBody(string Name);
public sealed record LinkedFolderBody(string Name, string Path);

public sealed record FolderBody(string Path);
public sealed record AppendBody(string Text, string? Heading);

public sealed record DecisionBody(bool Approve);

public sealed record CodeBody(string? Kind, string? To);

public sealed record TreeFolder(string Name, string Path, List<TreeFolder> Folders, List<TreeNode> Nodes);

public sealed record TreeNode(string Id, string Title, string Type, string Path, string? Modified, bool Archived);

public static class Json
{
    /// <summary>Converts request JSON values to plain CLR values for frontmatter.</summary>
    public static object? ToClr(System.Text.Json.JsonElement e) => e.ValueKind switch
    {
        System.Text.Json.JsonValueKind.String => e.GetString(),
        System.Text.Json.JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => null,
        System.Text.Json.JsonValueKind.Array => e.EnumerateArray().Select(ToClr).ToList(),
        System.Text.Json.JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => ToClr(p.Value)),
        _ => e.ToString()
    };

    public static Dictionary<string, object?>? ToClr(Dictionary<string, System.Text.Json.JsonElement>? map) =>
        map?.ToDictionary(kv => kv.Key, kv => ToClr(kv.Value));
}
