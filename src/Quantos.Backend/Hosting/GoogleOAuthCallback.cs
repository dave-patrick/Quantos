using System.Net;

namespace Quantos.Backend.Hosting;

/// <summary>
/// The one route Google redirects the browser to after consent. It is exempt from the Origin and Sec-Fetch-Site checks
/// (the redirect is cross-site) and from sign-in, so it matches only this exact path, accepts only GET, and acts only
/// while a sign-in that Quantos started is waiting and the state it generated comes back. Nothing it receives is logged.
/// </summary>
public static class GoogleOAuthCallback
{
    public const string Path = "/oauth/google/callback";

    /// <summary>True when this request was the callback and the response has been written.</summary>
    public static async Task<bool> TryHandleAsync(HttpContext ctx)
    {
        if (ctx.Request.Path.Value != Path) return false;
        var response = ctx.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'";
        if (!HttpMethods.IsGet(ctx.Request.Method))
        {
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return true;
        }
        var coordinator = ctx.RequestServices.GetService<GoogleTasksCoordinator>();
        var (status, message) = coordinator is null
            ? (400, "Quantos can't finish this sign-in.")
            : await coordinator.CompleteSignIn(ctx.Request.Query["code"], ctx.Request.Query["state"], ctx.RequestAborted);
        response.StatusCode = status;
        response.ContentType = "text/html; charset=utf-8";
        await response.WriteAsync(
            "<!doctype html><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Quantos</title>" +
            "<style>body{font:16px system-ui,sans-serif;max-width:32rem;margin:15vh auto;padding:0 1rem;color:#222}</style>" +
            "<h1>Quantos</h1><p>" + WebUtility.HtmlEncode(message) + "</p>");
        return true;
    }
}
