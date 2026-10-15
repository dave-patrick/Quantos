using System.Globalization;

namespace Quantos.Core;

/// <summary>Source of "now". Swappable in tests. Always local time with the device's current offset.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTimeOffset Now => DateTimeOffset.Now;
}

public static class Timestamps
{
    /// <summary>ISO 8601 with seconds and the UTC offset, e.g. 2026-10-15T14:05:00-07:00.</summary>
    public static string Format(DateTimeOffset value) =>
        value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>Sortable, filename-safe UTC stamp used for history and trash entries.</summary>
    public static string FileStamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
}
