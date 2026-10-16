using System.Collections.Concurrent;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;

namespace Quantos.Client;

/// <summary>
/// The local PowerShell terminal (BUILD-PLAN 8E.7), using the PowerShell engine built into .NET.
/// It runs only in the Windows client, separate from normal editing; a session must be approved in
/// a native prompt before it runs anything.
/// </summary>
public sealed class TerminalHost : IDisposable
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly Action<object> _send;

    public TerminalHost(Action<object> send) => _send = send;

    private sealed class Session(string id, Runspace runspace)
    {
        public string Id { get; } = id;
        public Runspace Runspace { get; } = runspace;
        public PowerShell? Running { get; set; }
        public string Cwd => Runspace.SessionStateProxy.Path.CurrentFileSystemLocation.Path;
    }

    public string Open(string cwd)
    {
        var state = InitialSessionState.CreateDefault2();
        var runspace = RunspaceFactory.CreateRunspace(state);
        runspace.Open();
        try { runspace.SessionStateProxy.Path.SetLocation(Directory.Exists(cwd) ? cwd : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)); }
        catch (Exception) { }
        var id = Guid.NewGuid().ToString("N")[..8];
        var session = new Session(id, runspace);
        _sessions[id] = session;
        var mode = runspace.SessionStateProxy.LanguageMode;
        _send(new { type = "terminal", @event = "opened", id, cwd = session.Cwd });
        _send(new { type = "terminal", @event = "output", id, cwd = session.Cwd,
            data = $"PowerShell {PSVersionInfo.PSVersion} · language mode: {mode}{(mode == PSLanguageMode.FullLanguage ? "" : " (app control is enforced on this machine)")}\n" });
        return id;
    }

    public void Input(string id, string line)
    {
        if (!_sessions.TryGetValue(id, out var session)) return;
        if (session.Running is not null) { _send(new { type = "terminal", @event = "output", id, data = "A command is still running. Press Ctrl+C to stop it.\n" }); return; }
        _send(new { type = "terminal", @event = "output", id, cwd = session.Cwd, data = $"PS {session.Cwd}> {line}\n" });
        if (line.Trim() is "cls" or "clear" or "Clear-Host") { _send(new { type = "terminal", @event = "clear", id }); return; }
        var ps = PowerShell.Create();
        ps.Runspace = session.Runspace;
        ps.AddScript(line).AddCommand("Out-String").AddParameter("Stream").AddParameter("Width", 160);
        session.Running = ps;
        _send(new { type = "terminal", @event = "state", id, running = true });

        var output = new PSDataCollection<PSObject>();
        output.DataAdded += (_, e) =>
        {
            var text = output[e.Index]?.ToString();
            if (text is not null) _send(new { type = "terminal", @event = "output", id, data = text + "\n" });
        };
        ps.Streams.Error.DataAdded += (_, e) => _send(new { type = "terminal", @event = "output", id, error = true, data = ps.Streams.Error[e.Index].ToString() + "\n" });
        ps.Streams.Warning.DataAdded += (_, e) => _send(new { type = "terminal", @event = "output", id, data = "WARNING: " + ps.Streams.Warning[e.Index].Message + "\n" });

        _ = Task.Run(() =>
        {
            try { ps.Invoke(null, output); }
            catch (Exception ex) { _send(new { type = "terminal", @event = "output", id, error = true, data = ex.Message + "\n" }); }
            finally
            {
                session.Running = null;
                ps.Dispose();
                _send(new { type = "terminal", @event = "state", id, running = false, cwd = SafeCwd(session) });
            }
        });
    }

    /// <summary>Runs one approved command to completion (for agent or CLI requests) and returns its text output.</summary>
    public static (string Output, bool Failed) RunOnce(string command, string cwd, TimeSpan timeout)
    {
        using var runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        runspace.Open();
        if (Directory.Exists(cwd)) runspace.SessionStateProxy.Path.SetLocation(cwd);
        using var ps = PowerShell.Create();
        ps.Runspace = runspace;
        ps.AddScript(command).AddCommand("Out-String").AddParameter("Width", 160);
        var async = ps.BeginInvoke();
        if (!async.AsyncWaitHandle.WaitOne(timeout)) { ps.Stop(); return ("Stopped: the command took longer than " + timeout.TotalSeconds + " seconds.", true); }
        var sb = new StringBuilder();
        foreach (var o in ps.EndInvoke(async)) sb.Append(o);
        foreach (var e in ps.Streams.Error) sb.AppendLine("ERROR: " + e);
        return (sb.ToString(), ps.HadErrors);
    }

    private static string SafeCwd(Session s) { try { return s.Cwd; } catch { return ""; } }

    public void Stop(string id)
    {
        if (_sessions.TryGetValue(id, out var s)) s.Running?.Stop();
    }

    public void Close(string id)
    {
        if (!_sessions.TryRemove(id, out var s)) return;
        try { s.Running?.Stop(); s.Runspace.Dispose(); } catch { }
        _send(new { type = "terminal", @event = "closed", id });
    }

    public void Dispose()
    {
        foreach (var id in _sessions.Keys) Close(id);
    }
}
