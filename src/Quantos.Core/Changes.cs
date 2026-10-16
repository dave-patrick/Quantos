using System.Text.Json.Serialization;

namespace Quantos.Core;

[JsonConverter(typeof(JsonStringEnumConverter<ChangeKind>))]
public enum ChangeKind { Created, Updated, Moved, Trashed, Restored, Deleted, Archived, Unarchived, Conflict, Reindexed, Notification, Approval, ShellJob }

public sealed record ChangeEvent(ChangeKind Kind, string? Id, string? Path, string? OldPath = null, string? Hash = null, string? Origin = null, object? Data = null);

/// <summary>Fans change events out to connected clients (write step 7).</summary>
public sealed class ChangeHub
{
    public event Action<ChangeEvent>? Changed;

    public void Publish(ChangeEvent e)
    {
        var handlers = Changed;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<ChangeEvent>>())
        {
            try { handler(e); } catch { /* a failing subscriber must not fail the write */ }
        }
    }
}
