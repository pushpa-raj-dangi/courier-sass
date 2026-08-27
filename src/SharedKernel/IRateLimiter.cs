namespace Courier.SharedKernel;

public record RateLimitResult(bool Allowed, TimeSpan RetryAfter);

public interface IRateLimiter
{
    Task<RateLimitResult> TryAcquireAsync(string partitionKey, int limit, TimeSpan window, CancellationToken cancellationToken = default);
}
