namespace Quantos.Client;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var paths = ClientPaths.Resolve(Arg(args, "--client-data"));
        ApplicationConfiguration.Initialize();

        if (args.Contains("--selfcheck"))
        {
            SelfCheck.RunAndShow(paths, null).GetAwaiter().GetResult();
            return 0;
        }

        // One client window per user; a second launch brings the first one forward.
        using var mutex = new Mutex(true, @"Local\Quantos.Client." + Environment.UserName, out var first);
        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Quantos.Client.Show." + Environment.UserName);
        if (!first)
        {
            signal.Set();
            return 0;
        }

        var form = new MainForm(paths, ClientPrefs.Load(paths));
        var watcher = new Thread(() =>
        {
            while (signal.WaitOne())
                if (form.IsHandleCreated) form.BeginInvoke(form.ShowMain);
        }) { IsBackground = true, Name = "quantos-show" };
        watcher.Start();

        Application.ThreadException += (_, e) => paths.Log("Unhandled: " + e.Exception);
        Application.Run(form);
        return 0;
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
