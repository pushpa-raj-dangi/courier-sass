namespace Courier.International.Application.Carriers;

// Fire-and-forget background work, so request paths (webhook, manual entry, inbound link) never
// wait on processing or a carrier poll. Implemented with Hangfire in Infrastructure.
public interface ICarrierJobScheduler
{
    void EnqueueProcessing();
    void EnqueuePoll(Guid organizationId, Guid bookingId);
}
