using System.Globalization;

namespace Quantos.Core;

/// <summary>Device-local time. Dates are recorded with the device's current offset (BUILD-PLAN section 15.11).</summary>
public class Clock(TimeProvider? provider = null)
{
    public TimeProvider Provider { get; } = provider ?? TimeProvider.System;

    public DateTimeOffset Now { get { RefreshZone(); return Provider.GetLocalNow(); } }
    public DateTimeOffset UtcNow => Provider.GetUtcNow();
    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
    public TimeZoneInfo Zone { get { RefreshZone(); return Provider.LocalTimeZone; } }

    // .NET caches the local time zone for the life of the process. Re-read it now and then, so a
    // laptop that changes time zone (travel, or a manual change) stamps new entries in the new zone.
    private long _zoneReadAt = Environment.TickCount64;
    private void RefreshZone()
    {
        if (!ReferenceEquals(Provider, TimeProvider.System)) return;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _zoneReadAt) < 30_000) return;
        Interlocked.Exchange(ref _zoneReadAt, now);
        TimeZoneInfo.ClearCachedData();
    }

    public string Stamp() => Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>Sortable UTC stamp used for history and trash names.</summary>
    public string FileStamp() => UtcNow.ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture);
}
