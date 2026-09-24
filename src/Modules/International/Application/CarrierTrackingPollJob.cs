using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Courier.International.Application;

// Hangfire recurring job "carrier-tracking-poll" (every 15 min); PollBookingAsync is enqueued when
// an inbound booking is linked on a polling account.
public class CarrierTrackingPollJob
{
    public const int MaxBookingsPerAccountPerRun = 50;
    private static readonly TimeSpan MaxBookingAge = TimeSpan.FromDays(60);

    private readonly DbContext _dbContext;
    private readonly ITenantScopeFactory _tenantScopeFactory;
    private readonly ICarrierJobScheduler _scheduler;

    public CarrierTrackingPollJob(DbContext dbContext, ITenantScopeFactory tenantScopeFactory, ICarrierJobScheduler scheduler)
    {
        _dbContext = dbContext;
        _tenantScopeFactory = tenantScopeFactory;
        _scheduler = scheduler;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Deliberate IgnoreQueryFilters: finds work across tenants; polling itself runs per tenant.
        var createdAfter = DateTimeOffset.UtcNow - MaxBookingAge;
        var targets = await (
            from booking in _dbContext.Set<CarrierBooking>().IgnoreQueryFilters()
            join account in _dbContext.Set<CarrierAccount>().IgnoreQueryFilters() on booking.CarrierAccountId equals account.Id
            where booking.Status == CarrierBookingStatus.Booked && booking.CreatedAt > createdAfter
                && account.IsActive && account.PollingEnabled
            orderby booking.CreatedAt
            select new { booking.Id, booking.OrganizationId, booking.CarrierAccountId })
            .ToListAsync(cancellationToken);

        var anyInserted = false;
        foreach (var organization in targets.GroupBy(t => t.OrganizationId))
        {
            await using var scope = _tenantScopeFactory.CreateScope(organization.Key);
            var poller = scope.ServiceProvider.GetRequiredService<CarrierBookingPoller>();
            foreach (var account in organization.GroupBy(t => t.CarrierAccountId))
            {
                var polled = 0;
                foreach (var target in account)
                {
                    if (polled >= MaxBookingsPerAccountPerRun)
                    {
                        break;
                    }
                    var result = await poller.PollAsync(target.Id, cancellationToken);
                    polled += result.Polled ? 1 : 0;
                    anyInserted |= result.Inserted > 0;
                }
            }
        }
        if (anyInserted)
        {
            _scheduler.EnqueueProcessing();
        }
    }

    public async Task PollBookingAsync(Guid organizationId, Guid bookingId, CancellationToken cancellationToken = default)
    {
        await using var scope = _tenantScopeFactory.CreateScope(organizationId);
        var result = await scope.ServiceProvider.GetRequiredService<CarrierBookingPoller>().PollAsync(bookingId, cancellationToken);
        if (result.Inserted > 0)
        {
            _scheduler.EnqueueProcessing();
        }
    }
}
