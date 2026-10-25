using System.Text;

namespace Quantos.Mobile;

internal sealed class AndroidTerminal(Action<object> reply, Func<string> directory) : IDisposable
{
    private readonly Dictionary<string, Java.Lang.Process> sessions = [];
    public void Open()
    {
        var id = Guid.NewGuid().ToString("N");
        var cwd = directory();
        var process = new Java.Lang.ProcessBuilder("/system/bin/sh", "-i").Directory(new Java.IO.File(cwd))!.RedirectErrorStream(true)!.Start()!;
        sessions.Add(id, process);
        reply(new { type = "terminal", @event = "opened", id, cwd });
        reply(new { type = "terminal", @event = "output", id, data = "Android sh · " + cwd + "\n" });
        _ = Task.Run(async () =>
        {
            try
            {
                using var reader = new StreamReader(process.InputStream!);
                var chars = new char[2048]; int count;
                while ((count = await reader.ReadAsync(chars)) > 0)
                    reply(new { type = "terminal", @event = "output", id, data = new string(chars, 0, count) });
                process.WaitFor();
            }
            catch (Exception) { /* Closing a shell also closes its pipe. */ }
            finally { reply(new { type = "terminal", @event = "closed", id }); }
        });
    }
    public void Input(string id, string text)
    {
        if (!sessions.TryGetValue(id, out var process)) return;
        var bytes = Encoding.UTF8.GetBytes(text + "\n");
        process.OutputStream!.Write(bytes); process.OutputStream.Flush();
    }
    public void Close(string id)
    {
        if (sessions.Remove(id, out var process)) { process.Destroy(); process.Dispose(); }
    }
    public void Dispose() { foreach (var id in sessions.Keys.ToArray()) Close(id); }
}
