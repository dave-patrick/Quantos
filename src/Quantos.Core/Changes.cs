using System.Text.Json.Serialization;

namespace Quantos.Core;

[JsonConverter(typeof(JsonStringEnumConverter<ChangeKind>))]
public enum ChangeKind { Created, Updated, Moved, Trashed, Restored, Deleted, Archived, Unarchived, Conflict, Reindexed, Notification, Approval, ShellJob }

public sealed record ChangeEvent(ChangeKind Kind, string? Id, string? Path, string? OldPath = null, string? Hash = null, string? Origin = null, object? Data = null)
{
    /// <summary>
    /// The node's list entry after the change (created, updated, moved, archived, restored), so clients
    /// can update their lists from the event itself instead of asking again. Null when not known.
    /// </summary>
    public Cache.NodeSummary? Summary { get; init; }

    /// <summary>Position in the backend's event stream (1, 2, 3 …). A client that sees a gap, or a
    /// smaller number after a restart, has missed events and reloads what it shows.</summary>
    public long Seq { get; init; }
}

/// <summary>Fans change events out to connected clients (write step 7).</summary>
public sealed class ChangeHub
{
    public event Action<ChangeEvent>? Changed;

    /// <summary>Looks up a node's list entry for <see cref="ChangeEvent.Summary"/>; null skips it.</summary>
    public Func<string, Cache.NodeSummary?>? Describe { get; set; }

    private static bool Describes(ChangeKind kind) =>
        kind is ChangeKind.Created or ChangeKind.Updated or ChangeKind.Moved or ChangeKind.Archived or ChangeKind.Unarchived or ChangeKind.Restored;

    public void Publish(ChangeEvent e)
    {
        var handlers = Changed;
        if (handlers is null) return;
        // Runs on the writing thread, right after the write, so the summary is the state just written.
        if (e.Summary is null && e.Id is not null && Describes(e.Kind) && Describe is { } describe)
        {
            try { e = e with { Summary = describe(e.Id) }; }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException or ObjectDisposedException) { }
        }
        foreach (var handler in handlers.GetInvocationList().Cast<Action<ChangeEvent>>())
        {
            try { handler(e); } catch { /* a failing subscriber must not fail the write */ }
        }
    }
}
