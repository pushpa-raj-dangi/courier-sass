namespace Courier.Riders.Application;

public record RiderTaskLoad(Guid RiderId, int OpenTaskCount);
public record DeliverySuccessStats(int Attempts, int Successes);
public record RiderSummary(int Total, int Active, int Inactive, int CurrentlyAssigned);

// DeliveryTask/DeliveryRun carry no OrganizationId of their own — every implementation of this
// interface must query them only through a join to Rider, never DbSet<DeliveryTask>()/
// DbSet<DeliveryRun>() standalone.
public interface IRiderReportingLookupService
{
    Task<IReadOnlyList<RiderTaskLoad>> GetBranchRiderTaskLoadAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>> GetSuccessRateByBranchAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>> GetSuccessRateByRiderAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<RiderSummary> GetSummaryAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
}
