using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Quantos.Backend.Hosting;

public enum SessionKind { App, Browser }

public sealed record SessionInfo(string Id, SessionKind Kind, DateTimeOffset Created);

/// <summary>
/// The launch token (new at every start, BUILD-PLAN section 10.8) and the one-time-code handoff
/// that gives the frontend an HttpOnly session cookie without the token ever reaching page script.
/// </summary>
public sealed class AccessTokens
{
    public const string CookieName = "quantos_session";
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, SessionKind Kind)> _codes = new();
    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();
    private readonly TimeProvider _time;

    public AccessTokens(TimeProvider? time = null, string? token = null)
    {
        _time = time ?? TimeProvider.System;
        LaunchToken = token ?? Random(32);
    }

    public string LaunchToken { get; }

    public bool IsLaunchToken(string? candidate) =>
        candidate is not null && CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(candidate), System.Text.Encoding.UTF8.GetBytes(LaunchToken));

    public string IssueCode(SessionKind kind)
    {
        var now = _time.GetUtcNow();
        foreach (var (code, entry) in _codes) if (entry.Expires < now) _codes.TryRemove(code, out _);
        var newCode = Random(24);
        _codes[newCode] = (now + CodeLifetime, kind);
        return newCode;
    }

    /// <summary>Redeems a code once. Expired or reused codes fail.</summary>
    public SessionInfo? Redeem(string? code)
    {
        if (code is null || !_codes.TryRemove(code, out var entry) || entry.Expires < _time.GetUtcNow()) return null;
        var session = new SessionInfo(Random(32), entry.Kind, _time.GetUtcNow());
        _sessions[session.Id] = session;
        return session;
    }

    public SessionInfo? Session(string? id) => id is not null && _sessions.TryGetValue(id, out var s) ? s : null;

    public void EndSession(string id) => _sessions.TryRemove(id, out _);

    private static string Random(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
