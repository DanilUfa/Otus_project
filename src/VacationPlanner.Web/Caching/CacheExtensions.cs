using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace VacationPlanner.Web.Caching;

/// <summary>Cache-aside хелпер над IDistributedCache.</summary>
public static class CacheExtensions
{
    public static async Task<T> GetOrSetAsync<T>(
        this IDistributedCache cache, string key, Func<Task<T>> load, TimeSpan ttl, CancellationToken ct)
    {
        var bytes = await cache.GetAsync(key, ct);
        if (bytes is not null && bytes.Length > 0)
            return JsonSerializer.Deserialize<T>(bytes)!;

        var value = await load();
        await cache.SetAsync(
            key,
            JsonSerializer.SerializeToUtf8Bytes(value),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
            ct);
        return value;
    }
}