using System.Text.Json;
using System.Text.Json.Serialization;
using Quantos.Core.Storage;

namespace Quantos.Core.GoogleTasks;

/// <summary>An explicit value, including an explicit JSON null, as opposed to "not sent".</summary>
public readonly record struct Optional<T>(bool HasValue, T? Value)
{
    public static Optional<T> Of(T? value) => new(true, value);
    public static Optional<T> None => default;
    public static implicit operator Optional<T>(T? value) => Of(value);
}

public sealed record GTaskList(string Id, string Title);

/// <summary>A Google task. <see cref="Due"/> is the UTC date part (yyyy-MM-dd); Google stores midnight UTC.</summary>
public sealed record GTask(string Id, string Title, string? Notes, string? Due, bool Completed, string? CompletedAt,
    string? Parent, string Updated, string? Etag, bool Deleted, bool Hidden, bool Assigned);

public sealed record GTaskPage(IReadOnlyList<GTask> Items, string? NextPageToken);

public sealed record GTaskWrite
{
    public Optional<string?> Title { get; init; }
    public Optional<string?> Notes { get; init; }
    public Optional<string?> Due { get; init; }
    public Optional<bool> Completed { get; init; }
}

/// <summary>A Google API call that failed. <see cref="Reason"/> is the first reason in the error body.</summary>
public sealed class GoogleApiException(int status, string? reason, string message, TimeSpan? retryAfter = null) : Exception(message)
{
    public int Status { get; } = status;
    public string? Reason { get; } = reason;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>The sign-in is no longer valid (revoked, expired, wrong scope): the person must connect again.</summary>
public sealed class GoogleAuthException(string message) : Exception(message);

public interface IGoogleTasksApi
{
    Task<IReadOnlyList<GTaskList>> ListTaskLists(CancellationToken ct);
    Task<GTaskList> CreateTaskList(string title, CancellationToken ct);
    /// <summary>Always asks for completed, deleted, hidden and assigned tasks, 100 per page.</summary>
    Task<GTaskPage> ListTasks(string listId, DateTimeOffset? updatedMin, string? pageToken, CancellationToken ct);
    /// <summary>Null on 404/410.</summary>
    Task<GTask?> Get(string listId, string id, CancellationToken ct);
    Task<GTask> Insert(string listId, GTaskWrite write, string? parent, CancellationToken ct);
    Task<GTask> Patch(string listId, string id, GTaskWrite write, CancellationToken ct);
    /// <summary>Omit the parent to move to the top level.</summary>
    Task<GTask> Move(string listId, string id, string? parent, CancellationToken ct);
    /// <summary>404 and 410 count as success.</summary>
    Task Delete(string listId, string id, CancellationToken ct);
}

public interface IGoogleTasksApiFactory
{
    IGoogleTasksApi Create();
    /// <summary>Discards any cached access token (after a disconnect, a new sign-in or new credentials).</summary>
    void Forget() { }
}

// ---- device-local configuration ---------------------------------------------------------------

public sealed record GoogleListMapping(string Workspace, string ListId, string ListTitle, DateTimeOffset? ImportCutoff = null, bool Confirmed = false);

public sealed record GoogleTasksConfig
{
    public string? ClientId { get; init; }
    public string? ProtectedClientSecret { get; init; }
    public string? ProtectedRefreshToken { get; init; }
    public int IntervalSeconds { get; init; } = 120;
    /// <summary>The vault this was set up for (pinned by the first save).</summary>
    public string? Vault { get; init; }
    public List<GoogleListMapping> Mappings { get; init; } = [];
}

/// <summary>Reads and writes google-tasks.json in the per-device settings folder. Secrets are sealed with the device key protector.</summary>
public sealed class GoogleTasksSettingsStore(string settingsFolder)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();
    private string File_ => Path.Combine(settingsFolder, "google-tasks.json");

    public GoogleTasksConfig Load()
    {
        lock (_gate)
        {
            try { return File.Exists(File_) ? JsonSerializer.Deserialize<GoogleTasksConfig>(AtomicFile.ReadAllText(File_), Json) ?? new() : new(); }
            catch (JsonException) { return new(); }
        }
    }

    public void Save(GoogleTasksConfig config)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(settingsFolder);
            AtomicFile.WriteAllText(File_, JsonSerializer.Serialize(config, Json));
        }
    }

    public void Delete()
    {
        lock (_gate) { if (File.Exists(File_)) File.Delete(File_); }
    }

    public static string Seal(string value) => Ai.AiSettingsStore.KeyProtector.Protect(value);

    /// <summary>Null if there is no value or it cannot be unlocked on this device.</summary>
    public static string? Unseal(string? sealedValue)
    {
        if (string.IsNullOrEmpty(sealedValue)) return null;
        try { return Ai.AiSettingsStore.KeyProtector.Unprotect(sealedValue); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
        catch (FormatException) { return null; }
    }
}

// ---- sync state (device-local, rebuildable from the gtask_* hints) -------------------------------

public sealed class FieldPair<T>
{
    public T? L { get; set; }
    public T? G { get; set; }
}

public sealed class LinkBase
{
    public FieldPair<string> Title { get; set; } = new();
    public FieldPair<string> Notes { get; set; } = new();
    public FieldPair<string> Due { get; set; } = new();
    public FieldPair<bool> Done { get; set; } = new();
    public FieldPair<string> Parent { get; set; } = new();
}

public sealed class GoogleLink
{
    public string Gid { get; set; } = "";
    public string ListId { get; set; } = "";
    public string? GoogleUpdated { get; set; }
    public int Missing { get; set; }
    public bool Suspended { get; set; }
    /// <summary>The next merge must ask Google for the task itself, because the incremental fetch may have moved past a change made while it was suspended or held back.</summary>
    public bool AskGoogle { get; set; }
    public LinkBase Base { get; set; } = new();
}

public sealed class GoogleIntent
{
    public string TaskId { get; set; } = "";
    public string ListId { get; set; } = "";
    public string? CursorAtIntent { get; set; }
    public string TitleNorm { get; set; } = "";
    public string? Due { get; set; }
}

public enum DeleteDirection { ToGoogle, ToQuantos }

public sealed class PendingDelete
{
    public string TaskId { get; set; } = "";
    public string Gid { get; set; } = "";
    public string ListId { get; set; } = "";
    public string Title { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter<DeleteDirection>))]
    public DeleteDirection Direction { get; set; }
    /// <summary>True when the deletion is waiting for the person (a batch over the limit); false for one left over from a transient failure, which is retried automatically.</summary>
    public bool Held { get; set; }
}

public sealed class GoogleTasksState
{
    public int Version { get; set; } = 1;
    public Dictionary<string, string?> Cursors { get; set; } = [];
    public Dictionary<string, GoogleLink> Links { get; set; } = [];
    public List<GoogleIntent> Intents { get; set; } = [];
    public List<PendingDelete> PendingDeletes { get; set; } = [];
    /// <summary>"listId|gid" → when a Google change first could not be applied; after a day the cursor is released so it cannot be pinned forever.</summary>
    public Dictionary<string, DateTimeOffset> HoldSince { get; set; } = [];
}

public interface IGoogleTasksStateStore
{
    GoogleTasksState Load();
    void Save(GoogleTasksState state);
    void Delete();
}

/// <summary>state.json under client-data/google-tasks/{vaultKey}/. A missing or damaged file is an empty state (links are rebuilt from the hints).</summary>
public sealed class FileGoogleTasksStateStore(string folder) : IGoogleTasksStateStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private string Path_ => Path.Combine(folder, "state.json");

    public static string VaultKey(string vaultRoot)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(vaultRoot.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public GoogleTasksState Load()
    {
        try { return File.Exists(Path_) ? JsonSerializer.Deserialize<GoogleTasksState>(AtomicFile.ReadAllText(Path_), Json) ?? new() : new(); }
        catch (JsonException) { return new(); }
    }

    public void Save(GoogleTasksState state)
    {
        Directory.CreateDirectory(folder);
        AtomicFile.WriteAllText(Path_, JsonSerializer.Serialize(state, Json));
    }

    /// <summary>Removes the state. A failure is thrown so the caller can tell the person it was kept.</summary>
    public void Delete()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}

public sealed record GoogleSyncReport(int Pushed, int Pulled, int Deleted, int Linked, int Skipped, int Conflicts, int PendingDeletes,
    IReadOnlyList<GoogleMappingProblem> Problems);

/// <summary>A mapping that could not be synced this run (for example its list was deleted on Google).</summary>
public sealed record GoogleMappingProblem(string Workspace, string Reason);

public sealed record GoogleSyncPreview(DateTimeOffset Cutoff, int CreateInGoogle, int ImportToQuantos, int Link, int Excluded);
