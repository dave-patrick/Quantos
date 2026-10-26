namespace Quantos.Core.Storage;

/// <summary>File access inside one approved directory, excluding junctions and symbolic links.</summary>
public static class FileBoundary
{
    public static bool ContainsFile(string directory, string path)
    {
        try
        {
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(path);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, comparison)) return false;
            if (Path.GetRelativePath(root, full).Contains(':')) return false; // NTFS alternate streams aren't attachment files.
            // A lexical path alone doesn't protect against a link within the approved directory.
            for (FileSystemInfo? item = new FileInfo(full); item is not null; item = item is FileInfo f ? f.Directory : ((DirectoryInfo)item).Parent)
            {
                if (item.LinkTarget is not null || item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                if (string.Equals(item.FullName.TrimEnd(Path.DirectorySeparatorChar), root, comparison)) return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { return false; }
    }
}
