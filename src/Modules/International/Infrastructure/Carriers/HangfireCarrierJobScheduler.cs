using Courier.International.Application;
using Courier.International.Application.Carriers;
using Hangfire;

namespace Courier.International.Infrastructure.Carriers;

public sealed class HangfireCarrierJobScheduler : ICarrierJobScheduler
{
    private readonly IBackgroundJobClient _backgroundJobClient;

    public HangfireCarrierJobScheduler(IBackgroundJobClient backgroundJobClient)
    {
        _backgroundJobClient = backgroundJobClient;
    }

    public void EnqueueProcessing() =>
        _backgroundJobClient.Enqueue<CarrierEventProcessorJob>(job => job.RunAsync(CancellationToken.None));

    public void EnqueuePoll(Guid organizationId, Guid bookingId) =>
        _backgroundJobClient.Enqueue<CarrierTrackingPollJob>(job => job.PollBookingAsync(organizationId, bookingId, CancellationToken.None));
}
