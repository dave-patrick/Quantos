using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.FileProviders;
using Quantos.Backend;
using Quantos.Core;

var options = BackendOptions.Parse(args);
BackendHost? host = null;
try
{
    host = BackendHost.Create(options);
    await host.App.StartAsync();
    host.AnnounceStarted();
    await host.App.WaitForShutdownAsync();
}
catch (QuantosException ex)
{
    Console.Error.WriteLine($"Quantos backend could not start: {ex.Message}");
    Environment.ExitCode = 2;
}
finally
{
    if (host is not null) await host.DisposeAsync();
}

namespace Quantos.Backend
{
    /// <summary>Builds and owns one backend instance for one vault. Used by Program and by the tests.</summary>
    public sealed class BackendHost : IAsyncDisposable
    {
        private readonly VaultLock _lock;
        private readonly string _runtimeFile;

        private BackendHost(WebApplication app, BackendOptions options, VaultLock vaultLock, WriteService writes, BackendRuntime runtime)
        {
            App = app;
            Options = options;
            _lock = vaultLock;
            Writes = writes;
            Runtime = runtime;
            _runtimeFile = Path.Combine(options.ClientDataPath, "runtime", "backend.json");
        }

        public WebApplication App { get; }
        public BackendOptions Options { get; }
        public WriteService Writes { get; }
        public BackendRuntime Runtime { get; }
        public string BaseUrl => $"http://127.0.0.1:{Runtime.Port}";

        public static BackendHost Create(BackendOptions options, bool fileLogging = true)
        {
            VaultLocation.EnsureCacheOutsideVault(options.VaultPath, options.ClientDataPath);
            var layout = new VaultLayout(options.VaultPath);
            Directory.CreateDirectory(options.ClientDataPath);
            var vaultLock = VaultLock.Acquire(layout); // Refuses a second backend on the same vault.

            try
            {
                var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
                builder.Logging.ClearProviders();
                builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
                builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
                if (fileLogging) builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(options.ClientDataPath, "logs")));

                // Loopback only (safeguard 6). Use the default port, or any free port if it's taken.
                var port = options.Port != 0 && PortIsFree(options.Port) ? options.Port : 0;
                builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, port));

                var runtime = new BackendRuntime(options.Token);
                var cache = new NodeCache(Path.Combine(options.ClientDataPath, "cache", "cache.sqlite"));
                var startupLog = new List<string>();
                var writes = new WriteService(layout, cache, log: startupLog.Add);
                writes.Start();

                builder.Services.AddSingleton(runtime);
                builder.Services.AddSingleton(writes);
                builder.Services.AddSingleton(options);
                builder.Services.ConfigureHttpJsonOptions(j => j.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

                var app = builder.Build();
                var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Quantos.Backend");
                foreach (var line in startupLog) log.LogInformation("{Message}", line);
                writes.Changed += (id, kind) => log.LogInformation("Node {Kind}: {Id}", kind, id);

                app.UseMiddleware<ApiErrors>();
                app.UseMiddleware<LocalSecurity>();
                var files = new ManifestEmbeddedFileProvider(typeof(BackendHost).Assembly, "wwwroot");
                app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = files,
                    OnPrepareResponse = ctx =>
                    {
                        ctx.Context.Response.Headers.CacheControl = "no-cache";
                        ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
                        ctx.Context.Response.Headers["Content-Security-Policy"] =
                            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'";
                        ctx.Context.Response.Headers["Referrer-Policy"] = "no-referrer";
                    },
                });
                Api.Map(app);

                var host = new BackendHost(app, options, vaultLock, writes, runtime);
                app.Lifetime.ApplicationStarted.Register(() =>
                {
                    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
                    runtime.Port = new Uri(address).Port;
                });
                return host;
            }
            catch
            {
                vaultLock.Dispose();
                throw;
            }
        }

        /// <summary>Writes the runtime file the Windows client and CLI use to connect, and prints the launch link.</summary>
        public void AnnounceStarted()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_runtimeFile)!);
            var info = new
            {
                port = Runtime.Port,
                token = Runtime.Token,
                pid = Environment.ProcessId,
                vault = Options.VaultPath,
                url = BaseUrl,
                startedAt = Timestamps.Format(DateTimeOffset.Now),
            };
            File.WriteAllText(_runtimeFile, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));

            var launch = $"{BaseUrl}/#token={Runtime.Token}";
            Console.WriteLine();
            Console.WriteLine($"Quantos backend is running for the vault at {Options.VaultPath}");
            foreach (var w in VaultLocation.Warnings(Options.VaultPath)) Console.WriteLine($"WARNING: {w}");
            Console.WriteLine($"Open Quantos in your browser: {launch}");
            Console.WriteLine("Keep this window open while you use Quantos. Close it (or press Ctrl+C) to stop.");
            if (Options.OpenBrowser && OperatingSystem.IsWindows())
            {
                try { Process.Start(new ProcessStartInfo(launch) { UseShellExecute = true }); }
                catch (Exception) { /* No default browser; the link is printed above. */ }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                // Only remove the runtime file if it still describes this process.
                if (File.Exists(_runtimeFile) && File.ReadAllText(_runtimeFile).Contains($"\"pid\": {Environment.ProcessId},"))
                    File.Delete(_runtimeFile);
            }
            catch (IOException) { }
            await App.DisposeAsync();
            _lock.Dispose();
        }

        private static bool PortIsFree(int port)
        {
            try
            {
                using var probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                probe.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }
    }

    /// <summary>Maps known errors to stable codes. Unknown errors are logged and returned without internals.</summary>
    public sealed class ApiErrors(RequestDelegate next, ILogger<ApiErrors> log)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await next(context);
            }
            catch (VersionConflictException ex)
            {
                context.Response.StatusCode = 409;
                await context.Response.WriteAsJsonAsync(new { error = ex.Code, message = ex.Message, currentVersion = ex.CurrentVersion });
            }
            catch (QuantosException ex)
            {
                context.Response.StatusCode = ex switch
                {
                    NotFoundException => 404,
                    VaultLockedException => 409,
                    _ => 400,
                };
                await context.Response.WriteAsJsonAsync(new { error = ex.Code, message = ex.Message });
            }
            catch (BadHttpRequestException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "invalid_request", message = ex.Message });
            }
            catch (Exception ex) when (!context.Response.HasStarted)
            {
                log.LogError(ex, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { error = "internal_error", message = "Something went wrong. Details are in the backend log." });
            }
        }
    }
}
