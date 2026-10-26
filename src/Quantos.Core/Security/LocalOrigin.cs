namespace Quantos.Core.Security;

/// <summary>Native bridges trust an exact loopback HTTP origin, never a URL's textual prefix.</summary>
public static class LocalOrigin
{
    public static bool Matches(string? candidate, string expected)
    {
        if (string.IsNullOrEmpty(candidate) || candidate.Any(char.IsControl) || candidate.Contains('\\')) return false;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && Uri.TryCreate(expected, UriKind.Absolute, out var origin)
            && origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback
            && uri.Scheme == origin.Scheme && uri.UserInfo.Length == 0
            && string.Equals(uri.IdnHost, origin.IdnHost, StringComparison.OrdinalIgnoreCase)
            && uri.Port == origin.Port;
    }
}
