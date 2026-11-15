namespace VetPos.Data.Services;

/// <summary>Dates in the shop's time (the PC's time zone); the database stores UTC.</summary>
public sealed class ShopClock(TimeProvider time)
{
    public DateTime UtcNow => time.GetUtcNow().UtcDateTime;
    public DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, time.LocalTimeZone);
    public DateOnly Today => DateOnly.FromDateTime(LocalNow);

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), time.LocalTimeZone);

    /// <summary>[from 00:00, the day after <paramref name="to"/> 00:00) in UTC, for date-range queries.</summary>
    public (DateTime FromUtc, DateTime ToUtc) Range(DateOnly from, DateOnly to)
    {
        if (to < from) (from, to) = (to, from);
        DateTime Utc(DateOnly d) => TimeZoneInfo.ConvertTimeToUtc(d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), time.LocalTimeZone);
        return (Utc(from), Utc(to.AddDays(1)));
    }
}
