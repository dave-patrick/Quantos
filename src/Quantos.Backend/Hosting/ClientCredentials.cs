using System.Security.Cryptography;
using System.Text.Json;
using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Storage;

namespace Quantos.Backend.Hosting;

public sealed record ClientCredentialInfo(string Id, ActorKind Kind, string Name, DateTimeOffset Created);
internal sealed record StoredClientCredential(string Id, ActorKind Kind, string Name, DateTimeOffset Created, string Hash);
public sealed record IssuedClientCredential(string Id, string Token)
{
    public override string ToString() => $"Issued credential {Id} (secret redacted)";
}
public sealed record PairingStatus(string State, string? Token = null)
{
    public override string ToString() => $"Pairing {State} (secret redacted)";
}

/// <summary>Persistent, revocable non-native identities. Only token hashes are kept by the server.</summary>
public sealed class ClientCredentials : IDisposable
{
    private readonly Lock gate = new();
    private readonly string file;
    private List<StoredClientCredential> credentials;
    private Dictionary<string, StoredClientCredential> byHash;
    private readonly Dictionary<string, Pairing> pending = new();
    private readonly Dictionary<string, CancellationTokenSource> streams = new();
    private readonly TimeProvider time;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Pairing(Actor Actor, string Proof, Approval Approval, ApprovalService Owner)
    {
        public string? Token { get; set; }
    }

    public ClientCredentials(QuantosPaths paths, TimeProvider? time = null)
    {
        file = Path.Combine(paths.Settings, "connection-identities.json");
        this.time = time ?? TimeProvider.System;
        credentials = File.Exists(file) ? JsonSerializer.Deserialize<List<StoredClientCredential>>(File.ReadAllText(file), Json) ?? [] : [];
        if (credentials.Count > 4096 || credentials.Any(c => c is null || c.Kind is not (ActorKind.Agent or ActorKind.Cli) || c.Kind == ActorKind.Cli && c.Name != "cli" || !Ulid.IsValid(c.Id) || !ValidName(c.Name) || c.Hash is not { Length: 64 } || c.Hash.Any(ch => !Uri.IsHexDigit(ch))))
            throw new QuantosException("Local connection identities are invalid. Restore the device settings before pairing again.");
        byHash = credentials.ToDictionary(c => c.Hash, StringComparer.OrdinalIgnoreCase);
    }

    public Actor? Authenticate(string token)
    {
        var hash = AccessTokens.Fingerprint(token);
        lock (gate)
        {
            var match = byHash.GetValueOrDefault(hash);
            if (match is not null && !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(match.Hash), Convert.FromHexString(hash))) return null;
            return match is null ? null : new Actor(match.Kind, match.Name, CredentialId: match.Id);
        }
    }

    // Trusted in-process entry point. HTTP pairing reaches this only after native approval.
    public IssuedClientCredential Issue(ActorKind kind, string name)
    {
        if (kind is not (ActorKind.Agent or ActorKind.Cli) || !ValidName(name) || kind == ActorKind.Cli && name != "cli") throw new QuantosException("Choose a valid agent or local CLI identity.");
        lock (gate)
        {
            if (credentials.Count >= 4096) throw new QuantosException("Remove unused connections before pairing another one.");
            var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var id = Ulid.New();
            var next = credentials.Append(new StoredClientCredential(id, kind, name, time.GetUtcNow(), AccessTokens.Fingerprint(token))).ToList();
            Save(next);
            return new(id, token);
        }
    }

    public IReadOnlyList<ClientCredentialInfo> List()
    {
        lock (gate) return credentials.Select(c => new ClientCredentialInfo(c.Id, c.Kind, c.Name, c.Created)).ToList();
    }

    public bool Revoke(string id)
    {
        CancellationTokenSource? stream;
        lock (gate)
        {
            if (!credentials.Any(c => c.Id == id)) return false;
            Save(credentials.Where(c => c.Id != id).ToList());
            streams.Remove(id, out stream);
        }
        if (stream is not null) { stream.Cancel(); stream.Dispose(); }
        return true;
    }

    public CancellationToken RevocationToken(string id)
    {
        lock (gate)
        {
            if (!credentials.Any(c => c.Id == id)) return new CancellationToken(true);
            if (!streams.TryGetValue(id, out var stream)) streams[id] = stream = new();
            return stream.Token;
        }
    }

    public void Dispose()
    {
        List<CancellationTokenSource> active;
        lock (gate) { active = streams.Values.ToList(); streams.Clear(); }
        foreach (var stream in active) { stream.Cancel(); stream.Dispose(); }
    }

    public Approval Request(ActorKind kind, string name, string proof, ApprovalService approvals)
    {
        if (kind is not (ActorKind.Agent or ActorKind.Cli) || !ValidName(name) || kind == ActorKind.Cli && name != "cli" || !ValidProof(proof)) throw new QuantosException("Choose a valid connection identity and pairing proof.");
        lock (gate)
        {
            foreach (var id in pending.Where(p => time.GetUtcNow() - p.Value.Approval.Requested > ApprovalService.Lifetime).Select(p => p.Key).ToList()) pending.Remove(id);
            if (pending.Count >= 32) throw new QuantosException("Too many recent pairing requests. Wait a few minutes before trying again.", 429);
            var actor = new Actor(kind, name);
            var code = AccessTokens.Fingerprint(proof)[..8].ToUpperInvariant();
            var rights = kind == ActorKind.Cli ? "This is a personal CLI connection: it can edit your vault and open signed-in browser windows." : "This agent will use only its own grants from Settings > Plugins & permissions.";
            var approval = approvals.Request(actor, Permission.ManagePermissions, $"Pair {kind} “{name}” with this device?\nPairing code: {code}\n\n{rights}\nOnly approve if this code matches the connection you started.");
            pending[approval.Id] = new(actor, AccessTokens.Fingerprint(proof), approval, approvals);
            return approval;
        }
    }

    public PairingStatus Poll(string id, string proof, ApprovalService approvals)
    {
        lock (gate)
        {
            if (!ValidProof(proof) || !pending.TryGetValue(id, out var pair) || !ReferenceEquals(pair.Owner, approvals)
                || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pair.Proof), Convert.FromHexString(AccessTokens.Fingerprint(proof))))
                throw new QuantosException("This pairing request is unavailable.", 404);
            if (time.GetUtcNow() - pair.Approval.Requested > ApprovalService.Lifetime) { pending.Remove(id); return new("Expired"); }
            // Calling Pending expires stale decisions before consuming one.
            approvals.Pending();
            if (pair.Token is not null) return Authenticate(pair.Token) is null ? new("Revoked") : new("Approved", pair.Token); // safe retry after a lost HTTP response, proof required
            if (pair.Approval.State == ApprovalState.Approved && approvals.Consume(id, pair.Actor, Permission.ManagePermissions, pair.Approval.Description))
                pair.Token = Issue(pair.Actor.Kind, pair.Actor.Name).Token;
            return new(pair.Token is not null ? "Approved" : pair.Approval.State.ToString(), pair.Token);
        }
    }

    private void Save(List<StoredClientCredential> next)
    {
        AtomicFile.WriteAllText(file, JsonSerializer.Serialize(next, Json));
        credentials = next;
        byHash = next.ToDictionary(c => c.Hash, StringComparer.OrdinalIgnoreCase);
    }
    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 128 && name == name.Trim() && !name.Any(char.IsControl);
    private static bool ValidProof(string? proof) => proof is { Length: 64 } && proof.All(Uri.IsHexDigit);
}
