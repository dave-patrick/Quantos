using System.Reflection;
using System.Text.Json;

namespace Quantos.Core.GoogleTasks;

/// <summary>
/// The Google Cloud client built into this copy of Quantos, if there is one (from the git-ignored build/google-client.json embedded at
/// build time). With it, the person only presses Connect; without it they paste their own client. A "Desktop app" client's secret is
/// not confidential in Google's sense, but builds that contain it are private and are not for publishing.
/// </summary>
public sealed record BuiltInGoogleClient(string? Id, string? Secret)
{
    public bool Exists => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Secret);

    public static BuiltInGoogleClient None { get; } = new(null, null);

    public static BuiltInGoogleClient Load()
    {
        try
        {
            using var stream = typeof(BuiltInGoogleClient).Assembly.GetManifestResourceStream("google-client.json");
            if (stream is null) return None;
            using var doc = JsonDocument.Parse(stream);
            string? Str(string name) => doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
            var client = new BuiltInGoogleClient(Str("clientId"), Str("clientSecret"));
            return client.Exists ? client : None;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return None; }
    }
}
