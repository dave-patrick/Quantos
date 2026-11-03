using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Quantos.Core.GoogleTasks;

public interface IGoogleAccessTokens
{
    /// <summary>A valid access token; <paramref name="forceRefresh"/> discards the cached one first.</summary>
    Task<string> Get(bool forceRefresh, CancellationToken ct);
}

public sealed record GoogleTokens(string AccessToken, string? RefreshToken, int ExpiresIn, string? Scope);

/// <summary>
/// Authorization code + PKCE for a Google "Desktop app" client: builds the consent URL, holds the one pending flow,
/// and exchanges, refreshes and revokes tokens. Secrets and codes never appear in exception text.
/// </summary>
public sealed class GoogleOAuth(HttpClient http, Uri? authBase = null, Uri? tokenBase = null, Uri? revokeBase = null)
{
    public const string Scope = "https://www.googleapis.com/auth/tasks";
    public static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);
    private static readonly Uri DefaultAuth = new("https://accounts.google.com/o/oauth2/v2/auth");
    private static readonly Uri DefaultToken = new("https://oauth2.googleapis.com/token");
    private static readonly Uri DefaultRevoke = new("https://oauth2.googleapis.com/revoke");

    private sealed record Flow(string State, string Verifier, string RedirectUri, DateTimeOffset Expires);
    private readonly object _gate = new();
    private Flow? _flow;
    private Func<DateTimeOffset> _now = () => DateTimeOffset.UtcNow;

    /// <summary>Tests replace the clock to expire flows.</summary>
    public void UseClock(Func<DateTimeOffset> now) => _now = now;

    public bool FlowPending { get { lock (_gate) return (_flow is not null && _flow.Expires > _now()) || (_deferred is { } d && d.Flow.Expires > _now()); } }

    public void CancelFlow() { lock (_gate) { _flow = null; _deferred = null; } }

    /// <summary>Starts (replacing any earlier) flow and returns the consent URL to open in the system browser.</summary>
    public string StartFlow(string clientId, string redirectUri)
    {
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        lock (_gate) { _flow = new Flow(state, verifier, redirectUri, _now() + FlowLifetime); _deferred = null; }
        var query = new (string, string)[]
        {
            ("response_type", "code"), ("client_id", clientId), ("redirect_uri", redirectUri), ("scope", Scope), ("state", state),
            ("code_challenge", challenge), ("code_challenge_method", "S256"), ("access_type", "offline"), ("prompt", "consent")
        };
        return (authBase ?? DefaultAuth) + "?" + string.Join('&', query.Select(p => p.Item1 + "=" + Uri.EscapeDataString(p.Item2)));
    }

    public enum CallbackResult { Completed, WrongState, NoFlow, Expired, Failed, Retry }

    /// <summary>
    /// Handles the redirect. A wrong state leaves the pending flow alone; a matching or expired one consumes it.
    /// </summary>
    public async Task<(CallbackResult Result, GoogleTokens? Tokens, string? Message)> Complete(string? code, string? state, string clientId, string clientSecret, CancellationToken ct)
    {
        Flow? flow;
        lock (_gate)
        {
            flow = _flow;
            if (flow is null) return (CallbackResult.NoFlow, null, null);
            if (flow.Expires <= _now()) { _flow = null; return (CallbackResult.Expired, null, null); }
            if (state is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(flow.State)))
                return (CallbackResult.WrongState, null, null);
            _flow = null;
        }
        if (string.IsNullOrEmpty(code)) return (CallbackResult.Failed, null, "Google did not send a sign-in code.");
        return await Exchange(code, flow, clientId, clientSecret, ct);
    }

    /// <summary>
    /// Android blocks the network of an app that is in the background, and during sign-in the browser is in front. A network
    /// failure therefore keeps the code (Google's lasts minutes) so the exchange can be tried again when the app is back in front.
    /// </summary>
    private (string Code, Flow Flow)? _deferred;

    public bool HasDeferred { get { lock (_gate) return _deferred is { } d && d.Flow.Expires > _now(); } }

    /// <summary>Tries the exchange again for a code kept by <see cref="Complete"/>; NoFlow when there is none.</summary>
    public async Task<(CallbackResult Result, GoogleTokens? Tokens, string? Message)> CompleteDeferred(string clientId, string clientSecret, CancellationToken ct)
    {
        (string Code, Flow Flow) d;
        lock (_gate)
        {
            if (_deferred is not { } kept) return (CallbackResult.NoFlow, null, null);
            if (kept.Flow.Expires <= _now()) { _deferred = null; return (CallbackResult.Expired, null, null); }
            d = kept;
            _deferred = null;
        }
        return await Exchange(d.Code, d.Flow, clientId, clientSecret, ct);
    }

    private async Task<(CallbackResult Result, GoogleTokens? Tokens, string? Message)> Exchange(string code, Flow flow, string clientId, string clientSecret, CancellationToken ct)
    {
        try
        {
            var tokens = await PostToken(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["code"] = code, ["code_verifier"] = flow.Verifier, ["redirect_uri"] = flow.RedirectUri,
                ["client_id"] = clientId, ["client_secret"] = clientSecret
            }, ct);
            if (tokens.Scope is null || !tokens.Scope.Split(' ').Contains(Scope))
                return (CallbackResult.Failed, null, "Tick the Google Tasks box on the consent screen and try again.");
            if (string.IsNullOrEmpty(tokens.RefreshToken))
                return (CallbackResult.Failed, null, "Google did not give Quantos a long-lived sign-in. Remove Quantos at myaccount.google.com/permissions and try again.");
            return (CallbackResult.Completed, tokens, null);
        }
        catch (GoogleAuthException ex) { return (CallbackResult.Failed, null, ex.Message); }
        catch (GoogleApiException ex) when (ex.Status == 503)
        {
            lock (_gate) _deferred = (code, flow with { Expires = _now() + TimeSpan.FromMinutes(8) });
            return (CallbackResult.Retry, null, ex.Message);
        }
        catch (GoogleApiException ex) { return (CallbackResult.Failed, null, ex.Message); }
    }

    public async Task<GoogleTokens> Refresh(string clientId, string clientSecret, string refreshToken, CancellationToken ct) =>
        await PostToken(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["client_id"] = clientId, ["client_secret"] = clientSecret
        }, ct);

    /// <summary>Best effort; the token goes in a form body, never the URL.</summary>
    public async Task<bool> Revoke(string token, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsync(revokeBase ?? DefaultRevoke, new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }), ct);
            // Google answers 400 for a token that is already revoked or expired, which is as good as revoked.
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.BadRequest;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    /// <summary>The consent page and nothing else: https, accounts.google.com, the v2 auth path, no user info.</summary>
    public static bool IsConsentUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host == "accounts.google.com" && u.IsDefaultPort
        && u.UserInfo.Length == 0 && u.AbsolutePath == "/o/oauth2/v2/auth";

    /// <summary>The first line of each message down the exception chain; the outer one alone can be a bare "Connection failure".</summary>
    internal static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null && parts.Count < 4; e = e.InnerException)
        {
            var line = e.Message.Split('\n')[0].Trim();
            parts.Add(e.GetType().Name + ": " + line);
        }
        return string.Join(" → ", parts);
    }

    private async Task<GoogleTokens> PostToken(Dictionary<string, string> form, CancellationToken ct)
    {
        HttpResponseMessage response;
        try { response = await http.PostAsync(tokenBase ?? DefaultToken, new FormUrlEncodedContent(form), ct); }
        catch (HttpRequestException ex) { throw new GoogleApiException(503, "network", "Can't reach Google: " + Describe(ex)); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new GoogleApiException(503, "timeout", "Google did not respond in time."); }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonElement root = default;
            try { root = JsonDocument.Parse(text).RootElement; } catch (JsonException) { }
            if (!response.IsSuccessStatusCode)
            {
                var error = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                if (error is "invalid_grant" or "invalid_client" or "unauthorized_client")
                    throw new GoogleAuthException(error == "invalid_grant" ? "Google no longer accepts this sign-in. Connect again." : "Google rejected the client ID or secret.");
                throw new GoogleApiException((int)response.StatusCode, error, "Google sign-in failed" + (error is null ? "." : $" ({error})."));
            }
            string? Str(string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var expires = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("expires_in", out var ex) && ex.TryGetInt32(out var n) ? n : 3600;
            var access = Str("access_token") ?? throw new GoogleApiException(502, null, "Google sent no access token.");
            return new GoogleTokens(access, Str("refresh_token"), expires, Str("scope"));
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Caches the access token until a minute before it expires and refreshes it from the sealed refresh token.</summary>
public sealed class GoogleTokenSource(GoogleOAuth oauth, Func<GoogleTasksConfig> config, Func<DateTimeOffset>? now = null) : IGoogleAccessTokens
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    private string? _token;
    private DateTimeOffset _expires;
    private int _generation;

    public async Task<string> Get(bool forceRefresh, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var cached = _token; // read once: Forget can clear the field at any moment
            if (!forceRefresh && cached is not null && _now() < _expires) return cached;
            var generation = Volatile.Read(ref _generation);
            var c = config();
            var secret = GoogleTasksSettingsStore.Unseal(c.ProtectedClientSecret);
            var refresh = GoogleTasksSettingsStore.Unseal(c.ProtectedRefreshToken);
            if (string.IsNullOrEmpty(c.ClientId) || secret is null || refresh is null)
                throw new GoogleAuthException("Connect Google Tasks again: the saved sign-in can't be unlocked on this device.");
            var tokens = await oauth.Refresh(c.ClientId, secret, refresh, ct);
            // A Forget during the refresh means the credentials changed: use this token once, but never cache it.
            if (generation != Volatile.Read(ref _generation)) return tokens.AccessToken;
            _token = tokens.AccessToken;
            _expires = _now().AddSeconds(Math.Max(60, tokens.ExpiresIn) - 60);
            return _token;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Drops the cached token, and any token a refresh in flight is about to cache.</summary>
    public void Forget()
    {
        Interlocked.Increment(ref _generation);
        _token = null;
    }
}
