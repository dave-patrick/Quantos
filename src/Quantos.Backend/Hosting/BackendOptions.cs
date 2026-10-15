namespace Quantos.Backend.Hosting;

/// <summary>Command-line options. The Windows client starts the backend with --parent so it stops if the client dies.</summary>
public sealed record BackendOptions
{
    public string? Vault { get; init; }
    public string? ClientData { get; init; }
    public int? Port { get; init; }
    public bool OpenBrowser { get; init; }
    public int? ParentPid { get; init; }
    public bool SelfCheck { get; init; }
    public bool Help { get; init; }

    public const int DefaultPort = 47821;

    public static BackendOptions Parse(string[] args)
    {
        var o = new BackendOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            o = args[i] switch
            {
                "--vault" => o with { Vault = Next() },
                "--client-data" => o with { ClientData = Next() },
                "--port" => o with { Port = int.Parse(Next()) },
                "--open" => o with { OpenBrowser = true },
                "--parent" => o with { ParentPid = int.Parse(Next()) },
                "--selfcheck" => o with { SelfCheck = true },
                "--help" or "-h" or "/?" => o with { Help = true },
                _ => o
            };
        }
        return o;
    }

    public const string Usage = """
        Quantos backend

          Quantos.Backend.exe [--vault <path>] [--open] [--port <n>] [--client-data <path>]

          --vault <path>        Vault to open (otherwise the last one used; first run asks in the app)
          --open                Open the app in your default browser once started
          --port <n>            Preferred local port (default 47821; a free port is used if it's taken)
          --client-data <path>  Settings, cache and logs folder (default: beside the application folder)
          --selfcheck           Check that this machine can run Quantos, print a report, and exit

        The backend listens on 127.0.0.1 only. Close it with Ctrl+C, or use Exit in the Windows client.
        """;
}
