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

    public static string ReadAllText(string path) => Decode(ReadAllBytes(path));

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Text from a file someone may have written with another editor: UTF-8 (with or without a BOM),
    /// UTF-16 with a BOM (Notepad's "Unicode"), or, when the bytes aren't valid UTF-8, the legacy
    /// Windows code page. Guessing beats replacing accented letters with "�", because Quantos
    /// writes the text back as UTF-8.
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..]) return Utf8.GetString(bytes); // keeps the BOM character, so it is preserved on save
        if (bytes is [0xFF, 0xFE, ..]) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes is [0xFE, 0xFF, ..]) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return Windows1252.GetString(bytes); }
    }

    private static Encoding Windows1252
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
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
            catch (IOException ex) when (attempt < 7 && IsTransient(ex)) { Thread.Sleep(50 * (attempt + 1)); }
            catch (UnauthorizedAccessException) when (attempt < 7) { Thread.Sleep(50 * (attempt + 1)); }
        }
    }

    private static bool IsTransient(IOException ex)
    {
        var code = ex.HResult & 0xFFFF;
        // Sharing and lock violations, a user-mapped section, and File.Replace failing because another
        // process briefly holds the target open (unable to remove the replaced file / move the replacement).
        return code is 32 or 33 or 1224 or 1175 or 1176 or 1177;
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
