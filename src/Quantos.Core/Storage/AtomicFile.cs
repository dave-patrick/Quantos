using System.Text;

namespace Quantos.Core.Storage;

/// <summary>
/// Writes so an interruption leaves either the old file or the new one, never a partial file:
/// temp file in the same folder, flushed to disk, then swapped in (STORAGE-SPEC section 6).
/// </summary>
public static class AtomicFile
{
    public const string TempSuffix = ".quantos-tmp";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string content) => WriteAllBytes(path, Utf8.GetBytes(content));

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + TempSuffix;
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        Retry(() =>
        {
            if (File.Exists(path)) File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else File.Move(temp, path);
        });
    }

    public static string ReadAllText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
    }

    public static byte[] ReadAllBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Moves a file, creating the destination folder. Fails if the destination exists.</summary>
    public static void Move(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Retry(() => File.Move(from, to, overwrite: false));
    }

    /// <summary>Sync tools and antivirus briefly lock files; retry sharing violations.</summary>
    public static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { action(); return; }
            catch (IOException ex) when (attempt < 5 && IsTransient(ex)) { Thread.Sleep(50 * (attempt + 1)); }
            catch (UnauthorizedAccessException) when (attempt < 5) { Thread.Sleep(50 * (attempt + 1)); }
        }
    }

    private static bool IsTransient(IOException ex)
    {
        var code = ex.HResult & 0xFFFF;
        return code is 32 or 33 or 1224; // sharing violation, lock violation, user-mapped section
    }

    /// <summary>Removes temp files left behind by an interrupted write.</summary>
    public static int CleanupTempFiles(string root)
    {
        var count = 0;
        if (!Directory.Exists(root)) return 0;
        foreach (var file in Directory.EnumerateFiles(root, "*" + TempSuffix, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
        {
            try { File.Delete(file); count++; } catch (IOException) { }
        }
        return count;
    }
}
