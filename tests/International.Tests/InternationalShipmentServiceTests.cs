using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.International.Application;
using Courier.International.Domain;
using Courier.International.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.International.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

// Records the last CreateShipmentShellAsync call and returns a fixed ShipmentResponse — the
// gateway's own creation logic is Shipments.Tests' responsibility (Task 6); this fake only proves
// InternationalShipmentService calls it with the right arguments and handles the response.
file class RecordingGateway : IShipmentInternationalGateway
{
    public CreateInternationalShipmentShellRequest? LastRequest { get; private set; }
    public Guid ShipmentId { get; } = Guid.NewGuid();

    public Task<ShipmentResponse> CreateShipmentShellAsync(CreateInternationalShipmentShellRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(new ShipmentResponse(
            ShipmentId, "AWB-INTL-1", request.MerchantId, request.OriginBranchId, request.DestinationBranchId,
            request.OriginBranchId, null, null, ShipmentStatus.Created, request.ServiceTypeId,
            request.WeightKg, request.LengthCm, request.WidthCm, request.HeightCm,
            request.Items.Sum(i => i.Quantity * i.UnitValue), 0m, false,
            request.ProvisionalTotalChargeAmount, 0m, 0m, request.ProvisionalTotalChargeAmount,
            request.RecipientName, request.RecipientPhone, request.RecipientAddressLine,
            request.SenderName, request.SenderPhone, request.SenderAddressLine,
            request.Items.Select(i => new ShipmentItemResponse(Guid.NewGuid(), i.Description, i.Quantity, i.UnitValue)).ToList(),
            DateTimeOffset.UtcNow));
    }

    public Task<ShipmentResponse?> TransitionToCustomsPreparedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

file class FixedCountryLookup : ICountryLookupService
{
    private readonly HashSet<string> _serviced;
    public FixedCountryLookup(params string[] serviced) => _serviced = serviced.ToHashSet(StringComparer.OrdinalIgnoreCase);
    public Task<bool> IsServicedAsync(string isoCode, CancellationToken cancellationToken = default) => Task.FromResult(_serviced.Contains(isoCode));
}

public class InternationalShipmentServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new InternationalLegConfiguration());
            modelBuilder.ApplyConfiguration(new CustomsItemConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    private static CreateInternationalShipmentRequest ValidRequest() => new(
        Guid.NewGuid(), ShipmentDirection.Outbound, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, null, null, null,
        "Foreign Recipient", "9998887776", null, null, null, null, "221B Baker Street", "London", null, "NW16XE", "GB",
        "Local Sender", "9811111111", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Ward 1 Line",
        null, null, null, null,
        5000m, "NPR", new List<CreateShipmentItemRequest> { new("Sample goods", 1, 5000m) });

    [Fact]
    public async Task CreateAsync_ServicedCountry_CallsGatewayAndPersistsInternationalLeg()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var gateway = new RecordingGateway();
        var service = new InternationalShipmentService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), gateway, new FixedCountryLookup("GB"));

        var result = await service.CreateAsync(ValidRequest(), ActingUser());

        Assert.NotNull(gateway.LastRequest);
        Assert.Equal("GB", gateway.LastRequest!.RecipientCountryCode);
        Assert.Equal(gateway.ShipmentId, result.Shipment.Id);
        Assert.False(result.Leg.CustomsCompleted);
        Assert.True(result.Leg.IsProvisionalPricing);
        Assert.Equal(5000m, result.Leg.ProvisionalTotalChargeAmount);

        var stored = await dbContext.Set<InternationalLeg>().SingleAsync(l => l.ShipmentId == gateway.ShipmentId);
        Assert.Equal(organizationId, stored.OrganizationId);
        Assert.Null(stored.CustomsCompletedAt);
    }

    [Fact]
    public async Task CreateAsync_UnservicedCountry_ThrowsAndNeverCallsGateway()
    {
        await using var dbContext = CreateContext();
        var gateway = new RecordingGateway();
        var service = new InternationalShipmentService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), gateway, new FixedCountryLookup());

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() => service.CreateAsync(ValidRequest(), ActingUser()));

        Assert.Null(gateway.LastRequest);
    }
}
