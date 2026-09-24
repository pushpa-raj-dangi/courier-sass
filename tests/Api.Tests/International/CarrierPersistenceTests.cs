using Courier.International.Domain;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierPersistenceTests : InternationalApiTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public CarrierPersistenceTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private async Task<(Guid OrganizationId, Guid LegId, Guid AccountId)> SeedAccountAndLegAsync(string label)
    {
        var org = await SetUpOrgAsync(label);
        var created = await CreateOutboundAsync(org);
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var legId = await dbContext.Set<InternationalLeg>().IgnoreQueryFilters()
            .Where(l => l.ShipmentId == created.Shipment.Id).Select(l => l.Id).SingleAsync();
        var account = new CarrierAccount
        {
            Id = Guid.NewGuid(), OrganizationId = org.Auth.OrganizationId, CarrierCode = "MANUAL", DisplayName = "Manual",
            WebhookPublicId = Guid.NewGuid().ToString("N"), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<CarrierAccount>().Add(account);
        await dbContext.SaveChangesAsync();
        return (org.Auth.OrganizationId, legId, account.Id);
    }

    private static CarrierBooking Booking(Guid organizationId, Guid legId, Guid accountId, CarrierBookingStatus status, string? awb = null) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = organizationId, InternationalLegId = legId, CarrierAccountId = accountId,
        Direction = CarrierBookingDirection.Outbound, Status = status, CarrierAwb = awb, IdempotencyKey = Guid.NewGuid().ToString("N"),
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task SecondLiveBookingForSameLeg_IsRejectedByTheDatabase_ButHistoryIsAllowed()
    {
        var (organizationId, legId, accountId) = await SeedAccountAndLegAsync("live-booking");
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        dbContext.Set<CarrierBooking>().Add(Booking(organizationId, legId, accountId, CarrierBookingStatus.Failed));
        dbContext.Set<CarrierBooking>().Add(Booking(organizationId, legId, accountId, CarrierBookingStatus.Booked, "AWB-1"));
        await dbContext.SaveChangesAsync();

        dbContext.Set<CarrierBooking>().Add(Booking(organizationId, legId, accountId, CarrierBookingStatus.Pending));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateCarrierEventIdForSameAccount_IsRejectedByTheDatabase()
    {
        var (organizationId, _, accountId) = await SeedAccountAndLegAsync("dup-event");
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        CarrierTrackingEvent Event() => new()
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, CarrierAccountId = accountId, CarrierAwb = "AWB-1",
            CarrierEventId = "evt-1", CarrierStatusCode = "IN_TRANSIT", OccurredAt = DateTimeOffset.UtcNow,
            Source = CarrierEventSource.Manual, ReceivedAt = DateTimeOffset.UtcNow
        };

        dbContext.Set<CarrierTrackingEvent>().Add(Event());
        await dbContext.SaveChangesAsync();
        dbContext.Set<CarrierTrackingEvent>().Add(Event());

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task CarrierAccounts_AreTenantFiltered()
    {
        var (organizationA, _, accountId) = await SeedAccountAndLegAsync("filter-a");
        var (organizationB, _, _) = await SeedAccountAndLegAsync("filter-b");
        var tenantScopes = Factory.Services.GetRequiredService<ITenantScopeFactory>();

        await using (var scopeA = tenantScopes.CreateScope(organizationA))
        {
            var visible = await scopeA.ServiceProvider.GetRequiredService<AppDbContext>().Set<CarrierAccount>().Select(a => a.Id).ToListAsync();
            Assert.Equal(new[] { accountId }, visible);
        }
        await using (var scopeB = tenantScopes.CreateScope(organizationB))
        {
            Assert.False(await scopeB.ServiceProvider.GetRequiredService<AppDbContext>().Set<CarrierAccount>().AnyAsync(a => a.Id == accountId));
        }
    }
}
