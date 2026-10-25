using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Quantos.Core.Ai;

namespace Quantos.Mobile;

/// <summary>Provider secrets are encrypted by a non-exportable Android Keystore key.</summary>
internal sealed class AndroidKeyProtector : IKeyProtector
{
    private const string Alias = "quantos-ai-providers";
    private static IKey Key()
    {
        var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        if (store.GetKey(Alias, null) is { } key) return key;
        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
        using var spec = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)!
            .SetBlockModes(KeyProperties.BlockModeGcm)!
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!.Build()!;
        generator.Init(spec);
        return generator.GenerateKey()!;
    }
    public string Protect(string value)
    {
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, Key());
        return Convert.ToBase64String(cipher.GetIV()!) + "." + Convert.ToBase64String(cipher.DoFinal(System.Text.Encoding.UTF8.GetBytes(value))!);
    }
    public string? Unprotect(string value)
    {
        try
        {
            var parts = value.Split('.');
            if (parts.Length != 2) return null;
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.DecryptMode, Key(), new GCMParameterSpec(128, Convert.FromBase64String(parts[0])));
            return System.Text.Encoding.UTF8.GetString(cipher.DoFinal(Convert.FromBase64String(parts[1]))!);
        }
        catch (Exception ex) when (ex is GeneralSecurityException or FormatException) { return null; }
    }
}
