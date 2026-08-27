using StackExchange.Redis;

namespace Courier.SharedKernel;

// Fixed-window counter, not sliding — a burst right at a window boundary can admit up to 2x
// the limit across the boundary. Accepted trade-off: a sliding-window implementation needs a
// sorted-set + per-request cleanup, meaningfully more Redis round-trips, for a guarantee this
// phase's rate limits (abuse prevention, not billing-grade precision) don't need.
public class RedisRateLimiter : IRateLimiter
{
    private readonly IConnectionMultiplexer _connection;

    public RedisRateLimiter(IConnectionMultiplexer connection)
    {
        _connection = connection;
    }

    public async Task<RateLimitResult> TryAcquireAsync(string partitionKey, int limit, TimeSpan window, CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        var windowBucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / (long)window.TotalSeconds;
        var key = $"ratelimit:{partitionKey}:{windowBucket}";

        var count = await db.StringIncrementAsync(key);
        if (count == 1)
        {
            await db.KeyExpireAsync(key, window);
        }

        if (count > limit)
        {
            var ttl = await db.KeyTimeToLiveAsync(key);
            return new RateLimitResult(false, ttl ?? window);
        }

        return new RateLimitResult(true, TimeSpan.Zero);
    }
}
