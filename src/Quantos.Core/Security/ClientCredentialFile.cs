using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Quantos.Core.Security;

// Also compiled into the thin CLI: no domain or third-party dependency.
public sealed record CachedClientCredential(string Kind, string Name, string Token)
{
    public override string ToString() => $"{Kind} credential for {Name} (secret redacted)";
}

public static class ClientCredentialFile
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Quantos paired client credential v1");
    public static string PathFor(string clientData, string kind, string name)
    {
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\0" + name)));
        return Path.Combine(clientData, "settings", "client-credentials", key + ".json");
    }

    public static CachedClientCredential? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var credential = JsonSerializer.Deserialize<CachedClientCredential>(File.ReadAllText(path));
        if (credential is null) return null;
        if (credential.Kind is not ("Agent" or "Cli") || string.IsNullOrWhiteSpace(credential.Name) || string.IsNullOrWhiteSpace(credential.Token))
            throw new IOException("The connection credential file is invalid. Pair it again.");
        if (OperatingSystem.IsWindows())
        {
            if (!credential.Token.StartsWith("dpapi:", StringComparison.Ordinal)) throw new IOException("This credential is not protected for this Windows user. Pair it again.");
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(credential.Token[6..]), Entropy, DataProtectionScope.CurrentUser);
            return credential with { Token = Encoding.UTF8.GetString(plain) };
        }
        if (credential.Token.StartsWith("dpapi:", StringComparison.Ordinal)) throw new IOException("This credential belongs to a Windows device. Pair it again on this device.");
        return credential;
    }

    public static void Write(string path, CachedClientCredential credential)
    {
        var stored = OperatingSystem.IsWindows()
            ? credential with { Token = "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(credential.Token), Entropy, DataProtectionScope.CurrentUser)) }
            : credential;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        if (new DirectoryInfo(directory).LinkTarget is not null) throw new IOException("A credential folder cannot be a symbolic link.");
        if (OperatingSystem.IsWindows())
        {
            var user = WindowsIdentity.GetCurrent().User ?? throw new IOException("The current Windows identity could not be established.");
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(acl);
        }
        else File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temp, options))
            {
                JsonSerializer.Serialize(stream, stored);
                stream.Flush(true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
