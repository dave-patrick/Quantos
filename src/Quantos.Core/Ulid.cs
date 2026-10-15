using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Quantos.Core;

/// <summary>Generates ULIDs: 48-bit millisecond timestamp + 80 random bits, Crockford base32.</summary>
public static partial class Ulid
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly object Gate = new();
    private static long _lastMs;
    private static readonly byte[] LastRandom = new byte[10];

    public static string New(DateTimeOffset? at = null)
    {
        var ms = (at ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();
        var random = new byte[10];
        lock (Gate)
        {
            if (ms == _lastMs)
            {
                // Monotonic within the same millisecond: increment the previous random part.
                LastRandom.CopyTo(random, 0);
                for (var i = 9; i >= 0 && ++random[i] == 0; i--) { }
            }
            else
            {
                RandomNumberGenerator.Fill(random);
                _lastMs = ms;
            }
            random.CopyTo(LastRandom, 0);
        }

        Span<char> chars = stackalloc char[26];
        for (var i = 9; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(ms & 31)];
            ms >>= 5;
        }
        // 80 random bits -> 16 chars.
        var bits = new System.Numerics.BigInteger(random, isUnsigned: true, isBigEndian: true);
        for (var i = 25; i >= 10; i--)
        {
            chars[i] = Alphabet[(int)(bits & 31)];
            bits >>= 5;
        }
        return new string(chars);
    }

    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    [GeneratedRegex("^[0-9A-HJKMNP-TV-Z]{26}$")]
    private static partial Regex Pattern();
}
