using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quantos.Core.Security;

/// <summary>Permission categories (BUILD-PLAN section 10).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Permission>))]
public enum Permission { Read, CreateEdit, MoveArchive, TrashRestore, PermanentDelete, RunCommands, OutsideFiles, Network, ManagePermissions }

[JsonConverter(typeof(JsonStringEnumConverter<ActorKind>))]
public enum ActorKind { User, Cli, Agent, Plugin, Automation, System }

/// <summary>Who is asking. <see cref="ViaBrowser"/> marks requests that arrived from a browser session.</summary>
public sealed record Actor(ActorKind Kind, string Name, bool ViaBrowser = false, string? Instance = null)
{
    public static readonly Actor User = new(ActorKind.User, "app");
    public static readonly Actor Browser = new(ActorKind.User, "browser", ViaBrowser: true);
    public static readonly Actor System = new(ActorKind.System, "quantos");
    public static readonly Actor Cli = new(ActorKind.Cli, "cli");

    public override string ToString() => $"{Kind}:{Name}" + (Instance is null ? "" : "#" + Instance);
}

public sealed class PermissionDeniedException(Actor actor, Permission permission, string reason)
    : Exception($"{Who(actor)} isn't allowed to {PermissionText.Describe(permission)}: {reason}")
{
    private static string Who(Actor a) => a.Kind switch
    {
        ActorKind.Plugin => $"The plugin “{a.Name}”",
        ActorKind.Agent => $"The agent “{a.Name}”",
        ActorKind.Automation => $"The automation “{a.Name}”",
        ActorKind.Cli => "The command line",
        ActorKind.System => "Quantos",
        _ => a.ViaBrowser ? "A browser session" : "This window"
    };


    public Actor Actor { get; } = actor;
    public Permission Permission { get; } = permission;
    public string Reason { get; } = reason;
}

public sealed class ApprovalRequiredException(Approval approval)
    : Exception($"Approval required in the Windows client: {approval.Description}")
{
    public Approval Approval { get; } = approval;
}

/// <summary>
/// Grants for agents, plugins and automations, stored in client-data (never in the vault, so vault
/// content can't grant itself permissions). Shell execution and permanent deletion always need an
/// approval from the Windows client, whoever asks.
/// </summary>
public sealed class PermissionService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _file;
    private Grants _grants;

    public PermissionService(string grantsFile, ApprovalService approvals)
    {
        _file = grantsFile;
        Approvals = approvals;
        _grants = Load();
    }

    public ApprovalService Approvals { get; }
    public bool SafeMode { get; set; }

    public static readonly Permission[] AlwaysApprove = [Permission.PermanentDelete, Permission.RunCommands, Permission.ManagePermissions];

    public void Demand(Actor actor, Permission permission, string description, string? approvalId = null)
    {
        if (actor.Kind == ActorKind.System) return;

        if (actor.Kind == ActorKind.Plugin && SafeMode)
            throw new PermissionDeniedException(actor, permission, "plugins are disabled in safe mode");

        if (!IsGranted(actor, permission))
            throw new PermissionDeniedException(actor, permission, "not granted");

        if (AlwaysApprove.Contains(permission))
        {
            if (approvalId is not null && Approvals.Consume(approvalId, permission, description)) return;
            throw new ApprovalRequiredException(Approvals.Request(actor, permission, description));
        }
    }

    public bool IsGranted(Actor actor, Permission permission) => actor.Kind switch
    {
        ActorKind.System => true,
        // The local user, through the app or a browser session, can do everything (subject to approvals).
        ActorKind.User => true,
        ActorKind.Cli => permission is not Permission.OutsideFiles and not Permission.Network and not Permission.ManagePermissions,
        _ => Lookup(actor).Contains(permission)
    };

    private HashSet<Permission> Lookup(Actor actor)
    {
        var table = actor.Kind switch
        {
            ActorKind.Agent => _grants.Agents,
            ActorKind.Plugin => _grants.Plugins,
            ActorKind.Automation => _grants.Automations,
            _ => null
        };
        if (table is not null && table.TryGetValue(actor.Name, out var granted)) return granted;
        // Defaults: agents may read; automations may edit content; plugins get nothing until granted.
        return actor.Kind switch
        {
            ActorKind.Agent => [Permission.Read],
            ActorKind.Automation => [Permission.Read, Permission.CreateEdit],
            _ => []
        };
    }

    public Grants Current => _grants;

    public void Set(ActorKind kind, string name, IEnumerable<Permission> permissions)
    {
        var table = kind switch
        {
            ActorKind.Agent => _grants.Agents,
            ActorKind.Plugin => _grants.Plugins,
            ActorKind.Automation => _grants.Automations,
            _ => throw new ArgumentException("Only agents, plugins and automations have configurable grants.")
        };
        table[name] = [.. permissions];
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        Storage.AtomicFile.WriteAllText(_file, JsonSerializer.Serialize(_grants, Json));
    }

    private Grants Load()
    {
        try
        {
            if (File.Exists(_file)) return JsonSerializer.Deserialize<Grants>(File.ReadAllText(_file), Json) ?? new Grants();
        }
        catch (JsonException) { }
        return new Grants();
    }

    public sealed class Grants
    {
        public Dictionary<string, HashSet<Permission>> Agents { get; set; } = new();
        public Dictionary<string, HashSet<Permission>> Plugins { get; set; } = new();
        public Dictionary<string, HashSet<Permission>> Automations { get; set; } = new();
    }
}

public enum ApprovalState { Pending, Approved, Denied, Used, Expired }

public sealed record Approval
{
    public required string Id { get; init; }
    public required Actor Actor { get; init; }
    public required Permission Permission { get; init; }
    public required string Description { get; init; }
    public required DateTimeOffset Requested { get; init; }
    public ApprovalState State { get; set; } = ApprovalState.Pending;
}

/// <summary>
/// One-shot approvals. Only the Windows client can approve (the API restricts the approve endpoint
/// to callers holding the launch token, which browser sessions never see).
/// </summary>
public sealed class ApprovalService(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Approval> _approvals = new();
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public event Action<Approval>? Requested;

    public Approval Request(Actor actor, Permission permission, string description)
    {
        Expire();
        var approval = new Approval { Id = Ulid.New(), Actor = actor, Permission = permission, Description = description, Requested = _time.GetUtcNow() };
        _approvals[approval.Id] = approval;
        Requested?.Invoke(approval);
        return approval;
    }

    public IReadOnlyList<Approval> Pending()
    {
        Expire();
        return _approvals.Values.Where(a => a.State == ApprovalState.Pending).OrderBy(a => a.Requested).ToList();
    }

    public Approval? Get(string id) => _approvals.GetValueOrDefault(id);

    public bool Decide(string id, bool approve)
    {
        Expire();
        if (!_approvals.TryGetValue(id, out var approval) || approval.State != ApprovalState.Pending) return false;
        approval.State = approve ? ApprovalState.Approved : ApprovalState.Denied;
        return true;
    }

    /// <summary>Uses an approval once. It must be approved, unexpired, and for the same permission and action.</summary>
    public bool Consume(string id, Permission permission, string description)
    {
        Expire();
        if (!_approvals.TryGetValue(id, out var approval)) return false;
        lock (approval)
        {
            if (approval.State != ApprovalState.Approved || approval.Permission != permission || approval.Description != description) return false;
            approval.State = ApprovalState.Used;
            return true;
        }
    }

    private void Expire()
    {
        var now = _time.GetUtcNow();
        foreach (var a in _approvals.Values)
            if (a.State is ApprovalState.Pending or ApprovalState.Approved && now - a.Requested > Lifetime) a.State = ApprovalState.Expired;
    }
}

/// <summary>Plain-language names for permissions, for messages and approval requests.</summary>
public static class PermissionText
{
    public static string Describe(Permission p) => p switch
    {
        Permission.Read => "read content",
        Permission.CreateEdit => "create or edit content",
        Permission.MoveArchive => "move or archive notes",
        Permission.TrashRestore => "move notes to the trash or restore them",
        Permission.PermanentDelete => "delete permanently",
        Permission.RunCommands => "run commands",
        Permission.OutsideFiles => "use files outside the vault",
        Permission.Network => "make network requests",
        Permission.ManagePermissions => "change permissions",
        _ => p.ToString()
    };
}