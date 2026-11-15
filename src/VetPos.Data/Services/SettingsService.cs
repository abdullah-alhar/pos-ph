using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using VetPos.Core.Domain;
using VetPos.Core.Rules;

namespace VetPos.Data.Services;

/// <summary>
/// Typed settings sections stored as JSON rows. Read often (cached), written rarely (audited).
/// A section that is missing or unreadable gives its defaults, so a bad value never stops the till.
/// </summary>
public sealed class SettingsService(IDbContextFactory<VetPosDbContext> dbFactory)
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private readonly ConcurrentDictionary<string, object> _cache = new();

    public event EventHandler<string>? Changed;

    public async Task<T> GetAsync<T>(string key, CancellationToken ct = default) where T : new()
    {
        if (_cache.TryGetValue(key, out var cached)) return (T)cached;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key, ct);
        var value = Parse<T>(row?.Value);
        _cache[key] = value!;
        return value;
    }

    /// <summary>Raw JSON of a section (for sections owned by other projects, e.g. the receipt design).</summary>
    public async Task<string?> GetRawAsync(string key, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key, ct))?.Value;
    }

    public Task SaveAsync<T>(string key, T value, Actor actor, CancellationToken ct = default) =>
        SaveRawAsync(key, JsonSerializer.Serialize(value, Json), actor, value!, ct);

    public async Task SaveRawAsync(string key, string json, Actor actor, object? cacheValue = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Settings.SingleOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) db.Settings.Add(row = new Setting { Key = key });
        if (row.Value == json) return;
        row.Value = json;
        Audit.Add(db, actor, AuditActions.SettingsChange, $"Settings changed: {key}", "Setting", null,
            newValue: key.Contains("ai", StringComparison.Ordinal) ? "(hidden)" : json);
        await db.SaveChangesAsync(ct);
        if (cacheValue is not null) _cache[key] = cacheValue;
        else _cache.TryRemove(key, out _);
        Changed?.Invoke(this, key);
    }

    private static T Parse<T>(string? json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json) ?? new T();
        }
        catch (JsonException)
        {
            return new T();
        }
    }
}
