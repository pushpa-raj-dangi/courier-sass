using Courier.SharedKernel;
using StackExchange.Redis;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class RedisIdempotencyStoreTests
{
    private static IConnectionMultiplexer CreateConnection() => ConnectionMultiplexer.Connect("localhost:6380");

    [Fact]
    public async Task GetAsync_NoRecordStored_ReturnsNull()
    {
        var store = new RedisIdempotencyStore(CreateConnection());
        var key = $"test-{Guid.NewGuid():N}";

        var result = await store.GetAsync(key);

        Assert.Null(result);
    }

    [Fact]
    public async Task StoreThenGet_RoundTripsExactRecord()
    {
        var store = new RedisIdempotencyStore(CreateConnection());
        var key = $"test-{Guid.NewGuid():N}";
        var record = new IdempotencyRecord("hash-abc", 201, "{\"id\":\"123\"}");

        await store.StoreAsync(key, record, TimeSpan.FromMinutes(5));
        var result = await store.GetAsync(key);

        Assert.NotNull(result);
        Assert.Equal(record.RequestBodyHash, result!.RequestBodyHash);
        Assert.Equal(record.StatusCode, result.StatusCode);
        Assert.Equal(record.ResponseBody, result.ResponseBody);
    }
}
