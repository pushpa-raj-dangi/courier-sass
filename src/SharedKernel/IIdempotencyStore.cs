namespace Courier.SharedKernel;

public record IdempotencyRecord(string RequestBodyHash, int StatusCode, string ResponseBody);

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task StoreAsync(string key, IdempotencyRecord record, TimeSpan ttl, CancellationToken cancellationToken = default);
}
