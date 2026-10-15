using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Quantos.Backend;

/// <summary>
/// Request checks for the local backend (BUILD-PLAN.md 10, safeguards 6, 8, 9):
///   - Only loopback connections.
///   - Host header must be 127.0.0.1:port or localhost:port (blocks DNS rebinding).
///   - If an Origin header is present it must be this backend's own origin (blocks other web pages).
///   - Every /api request must carry the per-launch token.
/// Static frontend files are public: they contain no vault data and can't do anything without the token.
/// </summary>
public sealed class LocalSecurity(RequestDelegate next, ILogger<LocalSecurity> log, BackendRuntime runtime)
{
    public const string TokenHeader = "X-Quantos-Token";

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var remote = context.Connection.RemoteIpAddress;
        if (remote is not null && !IPAddress.IsLoopback(remote))
        {
            await Reject(context, 403, "not_local", "Quantos only accepts connections from this computer.");
            return;
        }

        var port = runtime.Port;
        var host = request.Host;
        var hostOk = host.Port == port && (host.Host is "127.0.0.1" or "localhost");
        if (!hostOk)
        {
            await Reject(context, 400, "bad_host", "Unexpected Host header.");
            return;
        }

        if (request.Headers.Origin is { Count: > 0 } origins)
        {
            var origin = origins.ToString();
            if (origin != $"http://127.0.0.1:{port}" && origin != $"http://localhost:{port}")
            {
                await Reject(context, 403, "bad_origin", "Requests from other web pages aren't allowed.");
                return;
            }
        }

        if (request.Path.StartsWithSegments("/api") && !HasValidToken(request, runtime.Token))
        {
            await Reject(context, 401, "missing_token", "A valid access token is required.");
            return;
        }

        await next(context);
    }

    private static bool HasValidToken(HttpRequest request, string expected)
    {
        string? supplied = request.Headers[TokenHeader];
        if (string.IsNullOrEmpty(supplied))
        {
            var auth = request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.Ordinal)) supplied = auth[7..];
        }
        if (string.IsNullOrEmpty(supplied)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));
    }

    private async Task Reject(HttpContext context, int status, string code, string message)
    {
        log.LogWarning("Rejected {Method} {Path}: {Code}", context.Request.Method, context.Request.Path, code);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error = code, message });
    }
}

/// <summary>Live values the security layer needs. Port is known only after Kestrel binds.</summary>
public sealed class BackendRuntime(string token)
{
    public string Token { get; } = token;
    public int Port { get; set; }
}
