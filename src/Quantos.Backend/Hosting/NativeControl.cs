using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Quantos.Core;
using Quantos.Core.Agents;

namespace Quantos.Backend.Hosting;

public sealed record NativeConnection(string Token, string Url, int Port)
{
    public override string ToString() => "Native connection (credential redacted)";
}

/// <summary>Native reconnect is local IPC with OS peer identification, not a caller-selected HTTP header.</summary>
public static class NativeControl
{
    internal const byte WindowsClient = 1, OpenBrowser = 2;
    internal sealed record Reply(bool Ok, NativeConnection? Connection = null);
    internal static string PipeName(string clientData) => "quantos-native-" + AccessTokens.Fingerprint(Path.GetFullPath(clientData).TrimEnd('\\', '/').ToUpperInvariant())[..32];

    public static async Task<NativeConnection> ConnectWindowsAsync(string clientData, int serverPid, string trustedDirectory, CancellationToken ct = default)
    {
        var reply = await RequestAsync(clientData, serverPid, WindowsClient, ServerPaths(trustedDirectory), ct);
        return reply.Connection ?? throw new IOException("The backend did not grant a native connection. Update the client and backend together.");
    }

    public static async Task OpenBrowserAsync(string clientData, int serverPid, string trustedDirectory, CancellationToken ct = default) =>
        _ = await RequestAsync(clientData, serverPid, OpenBrowser, ServerPaths(trustedDirectory), ct);

    private static string[] ServerPaths(string directory) => [Path.Combine(directory, "Quantos.exe"), Path.Combine(directory, "Quantos.Backend.exe")];

    internal static async Task<Reply> RequestAsync(string clientData, int serverPid, byte operation, string[] trustedServers, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native reconnect requires Windows. Other native hosts own their backend in process.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var pipe = new NamedPipeClientStream(".", PipeName(clientData), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(5000, timeout.Token);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var actual) || actual != serverPid || !trustedServers.Any(path => SameExecutable((int)actual, path)))
            throw new IOException("The native channel is not owned by the expected Quantos backend.");
        await pipe.WriteAsync(new[] { operation }, timeout.Token);
        var length = new byte[4];
        await pipe.ReadExactlyAsync(length, timeout.Token);
        var count = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (count is < 1 or > 4096) throw new IOException("Invalid native response.");
        var bytes = new byte[count];
        await pipe.ReadExactlyAsync(bytes, timeout.Token);
        var reply = JsonSerializer.Deserialize<Reply>(bytes) ?? throw new IOException("Invalid native response.");
        if (!reply.Ok) throw new IOException("This executable is not authorized for the native channel. Use the matching installed Quantos client.");
        if (reply.Connection is { } connection && (!Uri.TryCreate(connection.Url, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port != connection.Port || uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0))
            throw new IOException("Invalid native backend address.");
        return reply;
    }

    internal static bool SameExecutable(int pid, string expected)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName is { } actual && string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException) { return false; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
}

internal sealed class NativeControlServer : BackgroundService
{
    private readonly QuantosPaths paths;
    private readonly ServerAddress address;
    private readonly AccessTokens tokens;
    private readonly ILogger<NativeControlServer> log;
    private readonly string windowsExecutable, backendExecutable;
    private readonly Action<string> openBrowser;

    internal NativeControlServer(QuantosPaths paths, ServerAddress address, AccessTokens tokens, ILogger<NativeControlServer> log,
        string? windowsExecutable = null, string? backendExecutable = null, Action<string>? openBrowser = null)
    {
        this.paths = paths; this.address = address; this.tokens = tokens; this.log = log;
        this.windowsExecutable = windowsExecutable ?? Path.Combine(McpService.AppDirectory(), "Quantos.exe");
        this.backendExecutable = backendExecutable ?? Path.Combine(McpService.AppDirectory(), "Quantos.Backend.exe");
        this.openBrowser = openBrowser ?? (url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows()) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(NativeControl.PipeName(paths.ClientData), PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var request = new byte[1];
                await pipe.ReadExactlyAsync(request, timeout.Token);
                var reply = new NativeControl.Reply(false);
                if (NativeControl.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid))
                {
                    if (request[0] == NativeControl.WindowsClient && NativeControl.SameExecutable((int)pid, windowsExecutable))
                        reply = new(true, new(tokens.IssueNativeToken("windows"), address.Url, address.Port));
                    else if (request[0] == NativeControl.OpenBrowser && NativeControl.SameExecutable((int)pid, backendExecutable))
                    {
                        openBrowser(address.Url + "auth/open?code=" + tokens.IssueCode(SessionKind.Browser));
                        reply = new(true);
                    }
                }
                var bytes = JsonSerializer.SerializeToUtf8Bytes(reply);
                var length = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
                await pipe.WriteAsync(length, timeout.Token);
                await pipe.WriteAsync(bytes, timeout.Token);
                await pipe.FlushAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException or Win32Exception)
            {
                log.LogWarning("Native control connection ended or was refused ({Kind}).", ex.GetType().Name);
                try { await Task.Delay(250, stoppingToken); } catch (OperationCanceledException) { return; }
            }
        }
    }
}
