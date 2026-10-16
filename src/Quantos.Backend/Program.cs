using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;
using Quantos.Backend.Api;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Security;
using Quantos.Core.Storage;

namespace Quantos.Backend;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        BackendOptions options;
        try { options = BackendOptions.Parse(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); Console.Error.WriteLine(BackendOptions.Usage); return 2; }
        if (options.Help) { Console.WriteLine(BackendOptions.Usage); return 0; }

        var paths = QuantosPaths.Resolve(options.ClientData);

        if (options.SelfCheck)
        {
            var results = SelfCheck.Run(paths);
            SelfCheck.Print(results, Console.Out);
            return results.Any(r => r.Status == "fail") ? 1 : 0;
        }

        paths.EnsureCreated();

        // One backend per client-data folder: hand off to a running one instead of starting a second writer.
        var existing = Discovery.Read(paths);
        if (existing is not null && await Discovery.IsAliveAsync(existing))
        {
            Console.WriteLine($"Quantos is already running at {existing.Url} (process {existing.Pid}).");
            if (options.OpenBrowser) await OpenBrowserAsync(existing.Url, existing.Token);
            return 0;
        }

        var app = Build(args, options, paths);
        await app.RunAsync();
        return 0;
    }

    public static WebApplication Build(string[] args, BackendOptions options, QuantosPaths paths, AccessTokens? tokens = null)
    {
        var settings = ClientSettings.Load(paths);
        var port = PickPort(options.Port ?? settings.Port ?? BackendOptions.DefaultPort);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseKestrel(k =>
        {
            k.Listen(IPAddress.Loopback, port); // local machine only (BUILD-PLAN section 10.6)
            k.AddServerHeader = false;
        });
        builder.Logging.AddProvider(new FileLoggerProvider(paths.Logs));
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(tokens ?? new AccessTokens());
        builder.Services.AddSingleton(new ServerAddress { Port = port });
        builder.Services.AddSingleton<VaultHost>();

        var app = builder.Build();
        var log = app.Services.GetRequiredService<ILogger<VaultHost>>();
        var host = app.Services.GetRequiredService<VaultHost>();

        app.Use(HandleErrors);
        app.UseMiddleware<RequestSecurity>();

        var files = new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot");
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            OnPrepareResponse = ctx =>
            {
                // Hashed assets can be cached; the shell must always be fresh.
                ctx.Context.Response.Headers.CacheControl = ctx.File.Name.EndsWith(".html") ? "no-store" : "public, max-age=31536000, immutable";
            }
        });

        NodeEndpoints.Map(app);
        SystemEndpoints.Map(app);
        TaskEndpoints.Map(app);
        VaultEndpoints.Map(app);
        StructureEndpoints.Map(app);
        RelationEndpoints.Map(app);
        app.MapFallback("/api/{**rest}", () => Results.NotFound(new { error = "No such endpoint." }));
        app.MapFallback(async ctx =>
        {
            // Client-side routes all load the shell.
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-store";
            await using var stream = files.GetFileInfo("index.html").CreateReadStream();
            await stream.CopyToAsync(ctx.Response.Body);
        });

        var vaultPath = options.Vault ?? settings.VaultPath;
        if (vaultPath is not null)
        {
            try { host.Open(vaultPath); }
            catch (Exception ex) { log.LogError(ex, "Couldn't open vault {Vault}", vaultPath); }
        }

        var address = app.Services.GetRequiredService<ServerAddress>();
        var accessTokens = app.Services.GetRequiredService<AccessTokens>();
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            Discovery.Write(paths, new DiscoveryInfo(port, Environment.ProcessId, accessTokens.LaunchToken, address.Url, host.Session?.Vault.Root, DateTimeOffset.Now, SystemEndpoints.Version));
            log.LogInformation("Quantos backend listening on {Url}", address.Url);
            Console.WriteLine($"Quantos backend {SystemEndpoints.Version} listening on {address.Url}");
            if (options.OpenBrowser) _ = OpenBrowserAsync(address.Url, accessTokens.LaunchToken);
        });
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            Discovery.Remove(paths, Environment.ProcessId);
            host.Dispose();
            log.LogInformation("Quantos backend stopped");
        });

        if (options.ParentPid is { } parent) WatchParent(parent, app.Lifetime);
        StartReminderLoop(host, log, app.Lifetime.ApplicationStopping);
        return app;
    }

    private static async Task HandleErrors(HttpContext ctx, Func<Task> next)
    {
        try { await next(); }
        catch (Exception ex) when (!ctx.Response.HasStarted)
        {
            var (status, body) = ex switch
            {
                QuantosException q => (q.Status, (object)new { error = q.Message }),
                ApprovalRequiredException a => (StatusCodes.Status428PreconditionRequired, new { error = a.Message, approval = a.Approval }),
                PermissionDeniedException p => (StatusCodes.Status403Forbidden, new { error = p.Message }),
                VaultLockedException v => (StatusCodes.Status409Conflict, new { error = v.Message }),
                BadHttpRequestException b => (b.StatusCode, new { error = b.Message }),
                FormatException f => (StatusCodes.Status400BadRequest, new { error = f.Message }),
                ArgumentException a => (StatusCodes.Status400BadRequest, new { error = a.Message }),
                UnauthorizedAccessException u => (StatusCodes.Status403Forbidden, new { error = u.Message }),
                IOException io => (StatusCodes.Status503ServiceUnavailable, new { error = "The file system refused the operation: " + io.Message }),
                _ => (StatusCodes.Status500InternalServerError, new { error = "Unexpected error: " + ex.Message })
            };
            if (status >= 500) ctx.RequestServices.GetRequiredService<ILogger<VaultHost>>().LogError(ex, "Request {Path} failed", ctx.Request.Path);
            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsJsonAsync(body);
        }
    }

    /// <summary>Checks reminders every 20 seconds. A missed reminder (sleep, restart) fires on the next tick.</summary>
    private static void StartReminderLoop(VaultHost host, ILogger log, CancellationToken stopping)
    {
        _ = Task.Run(async () =>
        {
            while (!stopping.IsCancellationRequested)
            {
                try { host.Session?.Reminders.Tick(); }
                catch (Exception ex) { log.LogWarning(ex, "Reminder check failed"); }
                try { await Task.Delay(TimeSpan.FromSeconds(20), stopping); } catch (OperationCanceledException) { }
            }
        }, stopping);
    }

    private static int PickPort(int preferred)
    {
        foreach (var candidate in new[] { preferred, 0 })
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, candidate);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
            catch (SocketException) { }
        }
        throw new InvalidOperationException("No local port is available.");
    }

    private static void WatchParent(int pid, IHostApplicationLifetime lifetime)
    {
        try
        {
            var parent = Process.GetProcessById(pid);
            parent.EnableRaisingEvents = true;
            parent.Exited += (_, _) => lifetime.StopApplication();
        }
        catch (ArgumentException) { lifetime.StopApplication(); }
    }

    /// <summary>Asks the backend for a one-time code and opens the default browser with it.</summary>
    public static async Task OpenBrowserAsync(string url, string token)
    {
        try
        {
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, url + "api/auth/code") { Content = JsonContent.Create(new { kind = "browser" }) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request);
            var payload = await response.Content.ReadFromJsonAsync<CodeResponse>();
            if (payload?.Url is { } openUrl) Process.Start(new ProcessStartInfo(openUrl) { UseShellExecute = true });
        }
        catch (Exception ex) { Console.Error.WriteLine("Couldn't open the browser: " + ex.Message); }
    }

    private sealed record CodeResponse(string Code, string Url);
}
