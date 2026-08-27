using Courier.SharedKernel;
using StackExchange.Redis;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class RedisRateLimiterTests
{
    private static IConnectionMultiplexer CreateConnection() => ConnectionMultiplexer.Connect("localhost:6380");

    [Fact]
    public async Task TryAcquireAsync_UnderLimit_ReturnsAllowedTrue()
    {
        var limiter = new RedisRateLimiter(CreateConnection());
        var partitionKey = $"test-{Guid.NewGuid():N}";

        var result = await limiter.TryAcquireAsync(partitionKey, limit: 5, window: TimeSpan.FromSeconds(10));

        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task TryAcquireAsync_ExceedsLimit_ReturnsAllowedFalseWithPositiveRetryAfter()
    {
        var limiter = new RedisRateLimiter(CreateConnection());
        var partitionKey = $"test-{Guid.NewGuid():N}";

        for (var i = 0; i < 3; i++)
        {
            await limiter.TryAcquireAsync(partitionKey, limit: 3, window: TimeSpan.FromSeconds(10));
        }
        var result = await limiter.TryAcquireAsync(partitionKey, limit: 3, window: TimeSpan.FromSeconds(10));

        Assert.False(result.Allowed);
        Assert.True(result.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task TryAcquireAsync_DifferentPartitionKeys_AreIndependent()
    {
        var limiter = new RedisRateLimiter(CreateConnection());
        var partitionA = $"test-a-{Guid.NewGuid():N}";
        var partitionB = $"test-b-{Guid.NewGuid():N}";

        for (var i = 0; i < 3; i++)
        {
            await limiter.TryAcquireAsync(partitionA, limit: 3, window: TimeSpan.FromSeconds(10));
        }
        var resultA = await limiter.TryAcquireAsync(partitionA, limit: 3, window: TimeSpan.FromSeconds(10));
        var resultB = await limiter.TryAcquireAsync(partitionB, limit: 3, window: TimeSpan.FromSeconds(10));

        Assert.False(resultA.Allowed);
        Assert.True(resultB.Allowed);
    }
}
