using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<CarrierTrackingPollJob> _logger;

    public CarrierTrackingPollJob(DbContext dbContext, ITenantScopeFactory tenantScopeFactory, ICarrierJobScheduler scheduler, ILogger<CarrierTrackingPollJob> logger)
    {
        _dbContext = dbContext;
        _tenantScopeFactory = tenantScopeFactory;
        _scheduler = scheduler;
        _logger = logger;
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
                    try
                    {
                        var result = await poller.PollAsync(target.Id, cancellationToken);
                        polled += result.Polled ? 1 : 0;
                        anyInserted |= result.Inserted > 0;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        // One poisoned booking (bad ciphertext, an unhandled provider error) must
                        // never abort the run for every other booking and tenant still to be
                        // polled — log it and move on.
                        _logger.LogError(ex, "Polling carrier booking {CarrierBookingId} failed unexpectedly.", target.Id);
                    }
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
        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<CarrierBookingPoller>().PollAsync(bookingId, cancellationToken);
            if (result.Inserted > 0)
            {
                _scheduler.EnqueueProcessing();
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Same isolation as RunAsync: this is a fire-and-forget enqueued job, so there is no
            // caller to propagate to — log it instead of letting Hangfire retry-storm a poisoned booking.
            _logger.LogError(ex, "Polling carrier booking {CarrierBookingId} failed unexpectedly.", bookingId);
        }
    }
}
