using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Events.Application;
using Courier.Geography.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.Shipments.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Shipments.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FakeBranchLookup : IBranchLookupService
{
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult<BranchZoneInfo?>(null);
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

file class AlwaysTrueGeographyLookup : IGeographyLookupService
{
    public Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class FixedOrganizationLookup : IOrganizationLookupService
{
    public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<PagedResult<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<string?> GetUserNameAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<string?>("testorg");
    public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<PagedResult<OrganizationRecord>> ListAsync(string? search, bool? isActive, bool? isDeleted, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<OrganizationRecord>(new List<OrganizationRecord>(), 1, 25, 0));
    public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class NoopCodCollectionCreationService : ICodCollectionCreationService
{
    public Task CreateExpectedAsync(Guid shipmentId, Guid organizationId, Guid merchantId, Guid branchId, decimal expectedAmount, decimal shipmentTotalCharge, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class ShipmentServiceInternationalTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
            modelBuilder.ApplyConfiguration(new ServiceTypeConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentItemConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentEventConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static ShipmentService CreateService(TestDbContext dbContext, Guid organizationId)
    {
        var branchLookup = new FakeBranchLookup();
        var pricingService = new PricingService(dbContext, new FixedTenantProvider(organizationId), branchLookup, new AlwaysTrueMerchantLookup());
        return new ShipmentService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchLookup,
            new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup(), pricingService, new AllBranchesAuthorizationChecker(),
            new FixedOrganizationLookup(), new NoopCodCollectionCreationService(), new NoopOutboxWriter());
    }

    private static CreateInternationalShipmentShellRequest OutboundRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        merchantId, ShipmentDirection.Outbound, originBranchId, destinationBranchId, serviceTypeId, 1m, null, null, null,
        "Foreign Recipient", "9998887776", null, null, null, null, "221B Baker Street", "London", null, "NW16XE", "GB",
        "Local Sender", "9811111111", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Ward 1 Line",
        null, null, null, null,
        5000m, "NPR", new List<CreateShipmentItemRequest> { new("Sample goods", 1, 5000m) });

    [Fact]
    public async Task CreateShipmentShellAsync_ValidOutbound_PersistsShipmentWithScopeAndDirection()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Org", Slug = "testorg" });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, organizationId);
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var result = await service.CreateShipmentShellAsync(
            OutboundRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId), ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.Created, result.CurrentStatus);
        var stored = await dbContext.Set<Shipment>().SingleAsync(s => s.Id == result.Id);
        Assert.Equal(ShipmentScope.International, stored.Scope);
        Assert.Equal(ShipmentDirection.Outbound, stored.Direction);
        Assert.Equal("GB", stored.RecipientCountryCode);
        Assert.Equal(5000m, stored.TotalCharge);
        Assert.Equal(0m, stored.CodAmount);
    }

    [Fact]
    public async Task TransitionToCustomsPreparedAsync_FromSorted_WhenOriginEqualsDestination_Succeeds()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Org", Slug = "testorg" });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, organizationId);
        var created = await service.CreateShipmentShellAsync(
            OutboundRequest(Guid.NewGuid(), branchId, branchId, serviceTypeId), ActingUser(Guid.NewGuid()));

        var shipment = await dbContext.Set<Shipment>().SingleAsync(s => s.Id == created.Id);
        shipment.CurrentStatus = ShipmentStatus.Sorted;
        await dbContext.SaveChangesAsync();

        var result = await service.TransitionToCustomsPreparedAsync(created.Id, ActingUser(Guid.NewGuid()));

        Assert.NotNull(result);
        Assert.Equal(ShipmentStatus.CustomsPrepared, result!.CurrentStatus);
    }

    [Fact]
    public async Task TransitionToCustomsPreparedAsync_FromSorted_WhenOriginDiffersFromDestination_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Org", Slug = "testorg" });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, organizationId);
        var created = await service.CreateShipmentShellAsync(
            OutboundRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), serviceTypeId), ActingUser(Guid.NewGuid()));

        var shipment = await dbContext.Set<Shipment>().SingleAsync(s => s.Id == created.Id);
        shipment.CurrentStatus = ShipmentStatus.Sorted;
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() =>
            service.TransitionToCustomsPreparedAsync(created.Id, ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task TransitionToCustomsPreparedAsync_FromAtDestinationBranch_WhenOriginDiffersFromDestination_Succeeds()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Org", Slug = "testorg" });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, organizationId);
        var created = await service.CreateShipmentShellAsync(
            OutboundRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), serviceTypeId), ActingUser(Guid.NewGuid()));

        var shipment = await dbContext.Set<Shipment>().SingleAsync(s => s.Id == created.Id);
        shipment.CurrentStatus = ShipmentStatus.AtDestinationBranch;
        await dbContext.SaveChangesAsync();

        var result = await service.TransitionToCustomsPreparedAsync(created.Id, ActingUser(Guid.NewGuid()));

        Assert.NotNull(result);
        Assert.Equal(ShipmentStatus.CustomsPrepared, result!.CurrentStatus);
    }

    [Fact]
    public async Task TransitionToCustomsPreparedAsync_OnDomesticShipment_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Org", Slug = "testorg" });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var domestic = new Shipment
        {
            Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, OrganizationId = organizationId, Awb = "DOM1",
            MerchantId = Guid.NewGuid(), OriginBranchId = Guid.NewGuid(), DestinationBranchId = Guid.NewGuid(),
            CurrentStatus = ShipmentStatus.Sorted, ServiceTypeId = serviceTypeId, WeightKg = 1m,
            Scope = ShipmentScope.Domestic, Direction = ShipmentDirection.Domestic,
            RecipientName = "R", RecipientPhone = "1", RecipientAddressLine = "L", SenderName = "S", SenderPhone = "2", SenderAddressLine = "L"
        };
        dbContext.Set<Shipment>().Add(domestic);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, organizationId);

        await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            service.TransitionToCustomsPreparedAsync(domestic.Id, ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task GetItemsAsync_ReturnsItemsForShipment_EmptyForUnknownShipment()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Org", Slug = "testorg" });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, organizationId);
        var created = await service.CreateShipmentShellAsync(
            OutboundRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), serviceTypeId), ActingUser(Guid.NewGuid()));

        var items = await service.GetItemsAsync(created.Id);
        Assert.Single(items);
        Assert.Equal("Sample goods", items[0].Description);

        var noItems = await service.GetItemsAsync(Guid.NewGuid());
        Assert.Empty(noItems);
    }
}
