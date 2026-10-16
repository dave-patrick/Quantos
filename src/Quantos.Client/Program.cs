namespace Quantos.Client;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var paths = ClientPaths.Resolve(Arg(args, "--client-data"));
        AppDomain.CurrentDomain.UnhandledException += (_, e) => paths.Log("Fatal: " + e.ExceptionObject);
        ApplicationConfiguration.Initialize();

        if (args.Contains("--selfcheck"))
        {
            if (args.Contains("--quiet"))
            {
                // Writes the report to client-data\logs and exits; for scripted or IT-run checks.
                // WebView2 needs the UI thread, so run the checks inside a hidden window's message loop.
                var exit = 2;
                var host = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized, Opacity = 0 };
                host.Load += async (_, _) =>
                {
                    try
                    {
                        var report = await SelfCheck.RunAsync(paths);
                        exit = report.Contains("[FAIL]") ? 1 : 0;
                        paths.Log("Self-check finished" + (exit == 1 ? " with failures" : ""));
                    }
                    catch (Exception ex) { paths.Log("Self-check crashed: " + ex); }
                    host.Close();
                };
                Application.Run(host);
                return exit;
            }
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
