using Quantos.Core.Security;

namespace Quantos.Backend.Hosting;

/// <summary>
/// Request checks (BUILD-PLAN section 10): Host header (blocks DNS rebinding), Origin and
/// Sec-Fetch-Site (blocks other web pages), and authentication by launch token or session cookie.
/// </summary>
public sealed class RequestSecurity(RequestDelegate next, AccessTokens tokens, ServerAddress address, ILogger<RequestSecurity> log)
{
    public const string ActorKey = "quantos.actor";
    public const string NativeKey = "quantos.native";
    public const string CsrfHeader = "X-Quantos-Request";
    public const string ClientHeader = "X-Quantos-Client";
    public const string AgentHeader = "X-Quantos-Agent";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var request = ctx.Request;

        if (!address.IsExpectedHost(request.Host))
        {
            log.LogWarning("Rejected request with unexpected Host {Host}", request.Host.Value);
            await Reject(ctx, StatusCodes.Status421MisdirectedRequest, "Unexpected host.");
            return;
        }

        var origin = request.Headers.Origin.ToString();
        if (origin.Length > 0 && !address.IsExpectedOrigin(origin))
        {
            log.LogWarning("Rejected request from origin {Origin}", origin);
            await Reject(ctx, StatusCodes.Status403Forbidden, "Unexpected origin.");
            return;
        }
        if (request.Headers["Sec-Fetch-Site"].ToString() == "cross-site")
        {
            await Reject(ctx, StatusCodes.Status403Forbidden, "Cross-site requests are not accepted.");
            return;
        }

        if (request.Path.StartsWithSegments("/auth/open"))
        {
            var session = tokens.Redeem(request.Query["code"]);
            if (session is null)
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync(ExpiredPage);
                return;
            }
            ctx.Response.Cookies.Append(AccessTokens.CookieName, session.Id, new CookieOptions
            {
                HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = false, Path = "/", IsEssential = true
            });
            ctx.Response.Headers.CacheControl = "no-store";
            var target = request.Query["to"].ToString();
            ctx.Response.Redirect(target.StartsWith('/') && !target.StartsWith("//") ? target : "/");
            return;
        }

        if (request.Path.StartsWithSegments("/api"))
        {
            var actor = Authenticate(ctx, out var native);
            if (actor is null)
            {
                await Reject(ctx, StatusCodes.Status401Unauthorized, "Open Quantos from the Windows client, or start the backend with --open.");
                return;
            }
            // Cookie-authenticated writes must carry a header a cross-site form or image can't send.
            if (!native && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && request.Headers[CsrfHeader].Count == 0)
            {
                await Reject(ctx, StatusCodes.Status403Forbidden, "Missing request header.");
                return;
            }
            var instance = ctx.Request.Headers["X-Quantos-Instance"].ToString();
            if (instance.Length is > 0 and <= 64) actor = actor with { Instance = instance };
            ctx.Items[ActorKey] = actor;
            ctx.Items[NativeKey] = native;

            // Agents and plugins act only within their grants: reading needs Read, and the app's own
            // controls (vault choice, safe mode, plugin switches, sign-in) are for the user alone.
            if (actor.Kind is ActorKind.Agent or ActorKind.Plugin or ActorKind.Automation)
            {
                var path = request.Path;
                if (path.StartsWithSegments("/api/setup") || path.StartsWithSegments("/api/system") || path.StartsWithSegments("/api/auth")
                    || path.StartsWithSegments("/api/plugins") || path.StartsWithSegments("/api/approvals"))
                {
                    await Reject(ctx, StatusCodes.Status403Forbidden, "Only you can do that, not an agent or plugin.");
                    return;
                }
                var permissions = ctx.RequestServices.GetService<VaultHost>()?.Session?.Permissions;
                if (permissions is not null && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) && !path.StartsWithSegments("/api/ping"))
                {
                    try { permissions.Demand(actor, Permission.Read, "Read vault content"); }
                    catch (PermissionDeniedException ex)
                    {
                        await Reject(ctx, StatusCodes.Status403Forbidden, ex.Message);
                        return;
                    }
                }
            }
        }

        if (request.Path.StartsWithSegments("/plugin-host"))
        {
            // The plugin host endpoint sets its own strict CSP with a per-response nonce.
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            await next(ctx);
            return;
        }
        ctx.Response.Headers.XContentTypeOptions = "nosniff";
        ctx.Response.Headers["X-Frame-Options"] = "DENY";
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
        ctx.Response.Headers.ContentSecurityPolicy =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'none'";
        await next(ctx);
    }

    private Actor? Authenticate(HttpContext ctx, out bool native)
    {
        native = false;
        var auth = ctx.Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.Ordinal) && tokens.IsLaunchToken(auth[7..].Trim()))
        {
            native = true;
            var agent = ctx.Request.Headers[AgentHeader].ToString();
            if (agent.Length > 0) return new Actor(ActorKind.Agent, agent);
            return ctx.Request.Headers[ClientHeader].ToString() switch
            {
                "windows" => new Actor(ActorKind.User, "windows-client"),
                "plugin" => new Actor(ActorKind.Plugin, ctx.Request.Headers["X-Quantos-Plugin"].ToString()),
                _ => Actor.Cli
            };
        }
        var session = tokens.Session(ctx.Request.Cookies[AccessTokens.CookieName]);
        if (session is null) return null;
        // Requests the app makes on a plugin's behalf carry its name and get only that plugin's grants.
        var plugin = ctx.Request.Headers["X-Quantos-Plugin"].ToString();
        if (plugin.Length > 0) return new Actor(ActorKind.Plugin, plugin, ViaBrowser: true);
        return session.Kind == SessionKind.App ? Actor.User with { ViaBrowser = true, Name = "app" } : Actor.Browser;
    }

    private static Task Reject(HttpContext ctx, int status, string message)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error = message });
    }

    private const string ExpiredPage = """
        <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Quantos</title>
        <style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#0c131d;color:#edf2fa;font:18px/1.6 system-ui,sans-serif}
        main{max-width:520px;padding:24px}h1{font-size:1.6rem}code{color:#78b6ff}</style></head>
        <body><main><h1>This link has expired</h1><p>Sign-in links work once and only for a minute. Open Quantos again from the Windows client,
        or run <code>Quantos.Backend.exe --open</code>.</p></main></body></html>
        """;
}

/// <summary>The address the backend actually bound, used for Host and Origin checks.</summary>
public sealed class ServerAddress
{
    public int Port { get; set; }
    public string Url => $"http://127.0.0.1:{Port}/";

    public bool IsExpectedHost(HostString host) =>
        host.Port == Port && (host.Host == "127.0.0.1" || string.Equals(host.Host, "localhost", StringComparison.OrdinalIgnoreCase));

    public bool IsExpectedOrigin(string origin) =>
        origin == $"http://127.0.0.1:{Port}" || string.Equals(origin, $"http://localhost:{Port}", StringComparison.OrdinalIgnoreCase);
}

public static class HttpContextActorExtensions
{
    public static Actor Actor(this HttpContext ctx) => (Actor)ctx.Items[RequestSecurity.ActorKey]!;
    public static bool IsNative(this HttpContext ctx) => ctx.Items[RequestSecurity.NativeKey] is true;
    public static bool IsWindowsClient(this HttpContext ctx) => ctx.IsNative() && ctx.Actor().Name == "windows-client";
}
