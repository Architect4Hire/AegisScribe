using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace AegisScribe.ApiService.Infrastructure.Idempotency;

// api-contract.md's 24-hour retention window — after that, a repeated key is treated as a new request.
public class RedisIdempotencyStore(IDistributedCache cache) : IIdempotencyStore
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24),
    };

    public async Task<IdempotencyRecord?> FindAsync(string key, CancellationToken ct)
    {
        var bytes = await cache.GetAsync(key, ct);
        return bytes is null ? null : JsonSerializer.Deserialize<IdempotencyRecord>(bytes);
    }

    public Task SaveAsync(string key, IdempotencyRecord record, CancellationToken ct) =>
        cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(record), CacheOptions, ct);
}
