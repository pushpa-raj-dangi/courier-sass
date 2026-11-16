namespace Courier.Cod.Application;

public record RiderHeldCash(decimal Amount, int ParcelCount, DateTimeOffset OldestCollectedAt);

// F15 R4 rider "my day": the rider's own undeposited COD — the row rule of
// CodService.GetRiderCashAsync (CodCollected and collected by this rider), summed across branches.
// Riders hold no cod.read, so this is only called for the rider resolved from the caller's JWT.
public interface ICodRiderCashLookupService
{
    Task<RiderHeldCash?> GetHeldByRiderAsync(Guid riderId, CancellationToken cancellationToken = default);
}
