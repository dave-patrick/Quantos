namespace Quantos.Client;

/// <summary>
/// Installs an update the backend downloaded and verified (client-data\updates\{version}\application). The running
/// program files are renamed to *.old (Windows allows renaming a running .exe) and the new ones copied in; any
/// failure puts the old ones back. The restarted client removes the *.old files once it's up.
/// </summary>
public static class Updater
{
    public static string UpdatesFolder(ClientPaths paths) => Path.Combine(paths.ClientData, "updates");

    public static string CurrentVersion => typeof(Updater).Assembly.GetName().Version?.ToString(4) ?? "0.0.0";

    /// <summary>The newest verified download newer than this build, or null.</summary>
    public static (string Version, string Folder)? Staged(ClientPaths paths)
    {
        var root = UpdatesFolder(paths);
        if (!Directory.Exists(root) || !Version.TryParse(CurrentVersion, out var current)) return null;
        return Directory.EnumerateDirectories(root)
            .Select(d => (Version: Path.GetFileName(d), Folder: d))
            .Where(x => Version.TryParse(x.Version, out var v) && v > current && File.Exists(Path.Combine(x.Folder, "ready.json")) && Directory.Exists(Path.Combine(x.Folder, "application")))
            .OrderByDescending(x => Version.Parse(x.Version))
            .Select(x => ((string, string)?)(x.Version, x.Folder)).FirstOrDefault();
    }

    /// <summary>True when Quantos runs from the zip layout (…\Quantos\application\), the only layout updates install into.</summary>
    public static bool CanInstall => string.Equals(new DirectoryInfo(ClientPaths.AppDirectory).Name, "application", StringComparison.OrdinalIgnoreCase);

    public static void Apply(string stagedFolder)
    {
        var appDir = ClientPaths.AppDirectory;
        var source = Path.Combine(stagedFolder, "application");
        var moved = new List<(string Dest, string Old)>();
        var copied = new List<string>();
        try
        {
            foreach (var file in Directory.GetFiles(source))
            {
                var dest = Path.Combine(appDir, Path.GetFileName(file));
                var old = dest + ".old";
                if (File.Exists(old)) File.Delete(old);
                if (File.Exists(dest)) { File.Move(dest, old); moved.Add((dest, old)); }
                File.Copy(file, dest);
                copied.Add(dest);
            }
        }
        catch
        {
            foreach (var c in copied) try { File.Delete(c); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            foreach (var (dest, old) in moved) try { File.Move(old, dest, overwrite: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>After a successful start: the replaced files and installed downloads go.</summary>
    public static void CleanUp(ClientPaths paths)
    {
        foreach (var old in Directory.EnumerateFiles(ClientPaths.AppDirectory, "*.old"))
            try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        var root = UpdatesFolder(paths);
        if (!Directory.Exists(root) || !Version.TryParse(CurrentVersion, out var current)) return;
        foreach (var dir in Directory.EnumerateDirectories(root))
            if (Version.TryParse(Path.GetFileName(dir), out var v) && v <= current)
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
