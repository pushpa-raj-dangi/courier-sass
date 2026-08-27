using System.Text.Json;
using StackExchange.Redis;

namespace Courier.SharedKernel;

public class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _connection;

    public RedisIdempotencyStore(IConnectionMultiplexer connection)
    {
        _connection = connection;
    }

    public async Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        var value = await db.StringGetAsync($"idem:{key}");
        if (!value.HasValue)
        {
            return null;
        }
        return JsonSerializer.Deserialize<IdempotencyRecord>((string)value!);
    }

    public async Task StoreAsync(string key, IdempotencyRecord record, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        await db.StringSetAsync($"idem:{key}", JsonSerializer.Serialize(record), ttl);
    }
}
