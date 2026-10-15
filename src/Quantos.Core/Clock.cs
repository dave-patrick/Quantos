using System.Globalization;

namespace Quantos.Core;

/// <summary>Device-local time. Dates are recorded with the device's current offset (BUILD-PLAN section 15.11).</summary>
public class Clock(TimeProvider? provider = null)
{
    public TimeProvider Provider { get; } = provider ?? TimeProvider.System;

    public DateTimeOffset Now => Provider.GetLocalNow();
    public DateTimeOffset UtcNow => Provider.GetUtcNow();
    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
    public TimeZoneInfo Zone => Provider.LocalTimeZone;

    public string Stamp() => Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>Sortable UTC stamp used for history and trash names.</summary>
    public string FileStamp() => UtcNow.ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture);
}
