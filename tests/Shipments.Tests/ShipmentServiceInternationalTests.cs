using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Events.Application;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
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

// Every branch id "exists" (in-tenant) unless listed in missingBranchIds — CreateShipmentShellAsync
// validates origin/destination existence through GetZoneInfoAsync (null == not found in tenant).
file class FakeBranchLookup : IBranchLookupService
{
    private readonly HashSet<Guid> _missingBranchIds;
    public FakeBranchLookup(params Guid[] missingBranchIds) => _missingBranchIds = missingBranchIds.ToHashSet();
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(_missingBranchIds.Contains(branchId) ? null : new BranchZoneInfo(branchId, null));
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

file class NeverFoundMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class SpecificBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly HashSet<Guid> _branchIds;
    public SpecificBranchesAuthorizationChecker(params Guid[] branchIds) => _branchIds = branchIds.ToHashSet();
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<Guid>?>(_branchIds);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(_branchIds.Contains(branchId));
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

    private static ShipmentService CreateService(
        DbContext dbContext, Guid organizationId,
        IBranchLookupService? branchLookup = null, IMerchantLookupService? merchantLookup = null, IBranchAuthorizationChecker? branchAuthorizationChecker = null)
    {
        branchLookup ??= new FakeBranchLookup();
        merchantLookup ??= new AlwaysTrueMerchantLookup();
        var pricingService = new PricingService(dbContext, new FixedTenantProvider(organizationId), branchLookup, merchantLookup);
        return new ShipmentService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchLookup,
            new AlwaysTrueGeographyLookup(), merchantLookup, pricingService, branchAuthorizationChecker ?? new AllBranchesAuthorizationChecker(),
            new FixedOrganizationLookup(), new NoopCodCollectionCreationService(), new NoopOutboxWriter());
    }

    private static async Task<(TestDbContext DbContext, Guid OrganizationId, Guid ServiceTypeId)> SeedAsync()
    {
        var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Org", Slug = "testorg" });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        await dbContext.SaveChangesAsync();
        return (dbContext, organizationId, serviceTypeId);
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

    [Fact]
    public async Task CreateShipmentShellAsync_MerchantNotInTenant_ThrowsAndPersistsNothing()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId, merchantLookup: new NeverFoundMerchantLookup());

        var ex = await Assert.ThrowsAsync<ShipmentValidationException>(() => service.CreateShipmentShellAsync(
            OutboundRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), serviceTypeId), ActingUser(Guid.NewGuid())));

        Assert.Contains("Merchant", ex.Message);
        Assert.Empty(await dbContext.Set<Shipment>().ToListAsync());
    }

    [Fact]
    public async Task CreateShipmentShellAsync_OriginBranchNotInTenant_ThrowsAndPersistsNothing()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var missingOrigin = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId, branchLookup: new FakeBranchLookup(missingOrigin));

        await Assert.ThrowsAsync<ShipmentValidationException>(() => service.CreateShipmentShellAsync(
            OutboundRequest(Guid.NewGuid(), missingOrigin, Guid.NewGuid(), serviceTypeId), ActingUser(Guid.NewGuid())));

        Assert.Empty(await dbContext.Set<Shipment>().ToListAsync());
    }

    [Fact]
    public async Task CreateShipmentShellAsync_DestinationBranchNotInTenant_ThrowsAndPersistsNothing()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var missingDestination = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId, branchLookup: new FakeBranchLookup(missingDestination));

        await Assert.ThrowsAsync<ShipmentValidationException>(() => service.CreateShipmentShellAsync(
            OutboundRequest(Guid.NewGuid(), Guid.NewGuid(), missingDestination, serviceTypeId), ActingUser(Guid.NewGuid())));

        Assert.Empty(await dbContext.Set<Shipment>().ToListAsync());
    }

    [Fact]
    public async Task CreateShipmentShellAsync_OriginOutsideCallersBranchAccess_Throws()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var grantedBranchId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId, branchAuthorizationChecker: new SpecificBranchesAuthorizationChecker(grantedBranchId));

        var ex = await Assert.ThrowsAsync<ShipmentValidationException>(() => service.CreateShipmentShellAsync(
            OutboundRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), serviceTypeId), ActingUser(Guid.NewGuid())));

        Assert.Contains("branch access", ex.Message);
        Assert.Empty(await dbContext.Set<Shipment>().ToListAsync());
    }

    [Fact]
    public async Task CreateShipmentShellAsync_OriginInsideCallersBranchAccess_Succeeds()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var grantedBranchId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId, branchAuthorizationChecker: new SpecificBranchesAuthorizationChecker(grantedBranchId));

        var result = await service.CreateShipmentShellAsync(
            OutboundRequest(Guid.NewGuid(), grantedBranchId, Guid.NewGuid(), serviceTypeId), ActingUser(Guid.NewGuid()));

        Assert.Equal(grantedBranchId, result.OriginBranchId);
    }

    [Theory]
    [InlineData(ShipmentDirection.Domestic)]
    [InlineData((ShipmentDirection)99)]
    public async Task CreateShipmentShellAsync_DirectionNotOutboundOrInbound_Throws(ShipmentDirection direction)
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);

        // Both parties carry a full Nepal chain AND InternationalAddress fields, so the address
        // validator alone would accept either direction interpretation — only the explicit
        // Direction guard can reject this.
        var request = OutboundRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), serviceTypeId) with
        {
            Direction = direction,
            RecipientProvinceId = Guid.NewGuid(), RecipientDistrictId = Guid.NewGuid(), RecipientLocalLevelId = Guid.NewGuid(), RecipientWardNumber = 1,
            SenderCity = "Kathmandu", SenderCountryCode = "GB"
        };

        var ex = await Assert.ThrowsAsync<ShipmentValidationException>(() => service.CreateShipmentShellAsync(request, ActingUser(Guid.NewGuid())));

        Assert.Contains("Direction", ex.Message);

        Assert.Empty(await dbContext.Set<Shipment>().ToListAsync());
    }

    public static TheoryData<string, List<CreateShipmentItemRequest>> InvalidShellItems() => new()
    {
        { "null item element", new List<CreateShipmentItemRequest> { null! } },
        { "zero quantity", new List<CreateShipmentItemRequest> { new("Sample goods", 0, 5000m) } },
        { "negative quantity", new List<CreateShipmentItemRequest> { new("Sample goods", -2, 5000m) } },
        { "negative unit value", new List<CreateShipmentItemRequest> { new("Sample goods", 1, -1m) } },
    };

    // Defense in depth — International.Application validates items up-front too, but a null element
    // must never reach the declared-value Sum (NullReferenceException → 500).
    [Theory]
    [MemberData(nameof(InvalidShellItems))]
    public async Task CreateShipmentShellAsync_InvalidItem_ThrowsValidationAndPersistsNothing(string label, List<CreateShipmentItemRequest> items)
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var request = OutboundRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), serviceTypeId) with { Items = items };

        var ex = await Assert.ThrowsAsync<ShipmentValidationException>(() => service.CreateShipmentShellAsync(request, ActingUser(Guid.NewGuid())));

        Assert.False(string.IsNullOrEmpty(ex.Message), label);
        Assert.Empty(await dbContext.Set<Shipment>().ToListAsync());
    }

    [Fact]
    public async Task CreateShipmentShellAsync_NepalSideInternationalFields_AreNotPersisted()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var request = OutboundRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), serviceTypeId) with
        {
            SenderCity = "Kathmandu", SenderRegion = "Bagmati", SenderPostalCode = "44600", SenderCountryCode = "US"
        };

        var result = await service.CreateShipmentShellAsync(request, ActingUser(Guid.NewGuid()));

        var stored = await dbContext.Set<Shipment>().SingleAsync(s => s.Id == result.Id);
        Assert.Null(stored.SenderCity);
        Assert.Null(stored.SenderRegion);
        Assert.Null(stored.SenderPostalCode);
        Assert.Null(stored.SenderCountryCode);
        Assert.Equal("GB", stored.RecipientCountryCode);
    }

    [Fact]
    public async Task TransitionToCustomsPreparedAsync_CurrentBranchOutsideCallersBranchAccess_Throws()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var branchId = Guid.NewGuid();
        var created = await CreateService(dbContext, organizationId).CreateShipmentShellAsync(
            OutboundRequest(Guid.NewGuid(), branchId, branchId, serviceTypeId), ActingUser(Guid.NewGuid()));
        var shipment = await dbContext.Set<Shipment>().SingleAsync(s => s.Id == created.Id);
        shipment.CurrentStatus = ShipmentStatus.Sorted;
        await dbContext.SaveChangesAsync();

        var restricted = CreateService(dbContext, organizationId, branchAuthorizationChecker: new SpecificBranchesAuthorizationChecker(Guid.NewGuid()));

        await Assert.ThrowsAsync<ShipmentValidationException>(() => restricted.TransitionToCustomsPreparedAsync(created.Id, ActingUser(Guid.NewGuid())));
        Assert.Equal(ShipmentStatus.Sorted, (await dbContext.Set<Shipment>().AsNoTracking().SingleAsync(s => s.Id == created.Id)).CurrentStatus);
    }

    // Real Postgres (TestDbContextFactory): ShipmentItem has no tenant query filter of its own, so
    // only the real AppDbContext's Shipment filter can prove GetItemsAsync's join through Shipment
    // hides another tenant's items (the InMemory TestDbContext above has no query filters at all).
    [Fact]
    public async Task GetScanInfoAsync_ReturnsShipmentDirection()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var created = await service.CreateShipmentShellAsync(
            OutboundRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), serviceTypeId), ActingUser(Guid.NewGuid()));

        var info = await service.GetScanInfoAsync(created.Id);

        Assert.Equal(ShipmentDirection.Outbound, info!.Direction);
    }

    [Fact]
    public async Task GetItemsAsync_AnotherTenantsShipment_ReturnsEmpty()
    {
        var tenantProvider = new MutableTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var provinceId = GeographyIds.ProvinceId("Bagmati");
        var districtId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
        var localLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
        dbContext.Add(new Organization { Id = orgA, Name = $"Org {orgA}", Slug = $"org-{orgA:N}" });
        dbContext.Add(new Organization { Id = orgB, Name = $"Org {orgB}", Slug = $"org-{orgB:N}" });
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = branchB, OrganizationId = orgB, Code = "ITB", Name = "Item Branch", Type = Courier.Branches.Domain.BranchType.Hub,
            ProvinceId = provinceId, DistrictId = districtId, LocalLevelId = localLevelId, WardNumber = 1, AddressLine = "Addr"
        });
        dbContext.Add(new Courier.Merchants.Domain.Merchant { Id = merchantB, OrganizationId = orgB, LegalName = "M Pvt Ltd", DisplayName = "M" });
        var shipmentId = Guid.NewGuid();
        dbContext.Add(new Shipment
        {
            Id = shipmentId, OrganizationId = orgB, Awb = $"AWB{Guid.NewGuid():N}"[..12], MerchantId = merchantB,
            OriginBranchId = branchB, DestinationBranchId = branchB, CurrentBranchId = branchB, CurrentStatus = ShipmentStatus.Created,
            ServiceTypeId = PricingIds.ServiceTypeId(ServiceTypeCode.Standard), WeightKg = 1m,
            RecipientName = "R", RecipientPhone = "1", RecipientAddressLine = "L",
            RecipientProvinceId = provinceId, RecipientDistrictId = districtId, RecipientLocalLevelId = localLevelId, RecipientWardNumber = 1,
            SenderName = "S", SenderPhone = "2", SenderAddressLine = "L",
            SenderProvinceId = provinceId, SenderDistrictId = districtId, SenderLocalLevelId = localLevelId, SenderWardNumber = 1
        });
        dbContext.Add(new ShipmentItem { Id = Guid.NewGuid(), ShipmentId = shipmentId, Description = "Other tenant item", Quantity = 1, UnitValue = 1m });
        await dbContext.SaveChangesAsync();

        tenantProvider.OrganizationId = orgB;
        Assert.Single(await CreateService(dbContext, orgB).GetItemsAsync(shipmentId));

        tenantProvider.OrganizationId = orgA;
        Assert.Empty(await CreateService(dbContext, orgA).GetItemsAsync(shipmentId));
    }

    private sealed class MutableTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId { get; set; }
    }

    private static CreateInternationalShipmentShellRequest InboundRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        merchantId, ShipmentDirection.Inbound, originBranchId, destinationBranchId, serviceTypeId, 1m, null, null, null,
        "Local Recipient", "9811111111", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Ward 1 Line",
        null, null, null, null,
        "Foreign Sender", "9998887776", null, null, null, null, "221B Baker Street", "London", null, "NW16XE", "GB",
        5000m, "NPR", new List<CreateShipmentItemRequest> { new("Sample goods", 1, 5000m) });

    // Creates an international shipment through the real gateway, then positions it at `status`.
    private static async Task<Guid> CreateAtStatusAsync(
        TestDbContext dbContext, ShipmentService service, Guid serviceTypeId, ShipmentDirection direction, ShipmentStatus status,
        Guid? originBranchId = null, Guid? destinationBranchId = null, Guid? currentBranchId = null)
    {
        var origin = originBranchId ?? Guid.NewGuid();
        var destination = destinationBranchId ?? Guid.NewGuid();
        var request = direction == ShipmentDirection.Outbound
            ? OutboundRequest(Guid.NewGuid(), origin, destination, serviceTypeId)
            : InboundRequest(Guid.NewGuid(), origin, destination, serviceTypeId);
        var created = await service.CreateShipmentShellAsync(request, ActingUser(Guid.NewGuid()));
        var shipment = await dbContext.Set<Shipment>().SingleAsync(s => s.Id == created.Id);
        shipment.CurrentStatus = status;
        shipment.CurrentBranchId = currentBranchId;
        await dbContext.SaveChangesAsync();
        return created.Id;
    }

    private static CarrierEventReference CarrierRef(string code = "IN_TRANSIT") =>
        new(Guid.NewGuid(), "CARRIER-AWB-1", $"evt-{Guid.NewGuid():N}", code, "Frankfurt");

    [Fact]
    public async Task ApplyCarrierStatusAsync_AllowedTransition_AppliesWithCarrierTimestampAndNoUser()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.HandedToCarrier);
        var occurredAt = DateTimeOffset.UtcNow.AddMinutes(1);

        var result = await service.ApplyCarrierStatusAsync(shipmentId, ShipmentStatus.InInternationalTransit, occurredAt, CarrierRef());

        Assert.Equal(CarrierStatusApplyResult.Applied, result);
        var shipment = await dbContext.Set<Shipment>().SingleAsync(s => s.Id == shipmentId);
        Assert.Equal(ShipmentStatus.InInternationalTransit, shipment.CurrentStatus);
        var evt = await dbContext.Set<ShipmentEvent>().SingleAsync(e => e.ShipmentId == shipmentId && e.EventType == ShipmentEventType.InInternationalTransit);
        Assert.Null(evt.UserId);
        Assert.Equal(occurredAt, evt.Timestamp);
        Assert.Equal(ShipmentStatus.HandedToCarrier, evt.PreviousStatus);
        Assert.Contains("\"source\":\"Carrier\"", evt.Metadata);
        Assert.Contains("CARRIER-AWB-1", evt.Metadata);
    }

    [Fact]
    public async Task ApplyCarrierStatusAsync_OccurredBeforeTheLatestEvent_IsStampedAfterItAndKeepsTheCarrierTimeInMetadata()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.HandedToCarrier);
        var latestBefore = await dbContext.Set<ShipmentEvent>().Where(e => e.ShipmentId == shipmentId).MaxAsync(e => e.Timestamp);
        var occurredAt = latestBefore.AddHours(-3);

        await service.ApplyCarrierStatusAsync(shipmentId, ShipmentStatus.InInternationalTransit, occurredAt, CarrierRef());

        var evt = await dbContext.Set<ShipmentEvent>().SingleAsync(e => e.ShipmentId == shipmentId && e.EventType == ShipmentEventType.InInternationalTransit);
        Assert.True(evt.Timestamp > latestBefore, $"expected {evt.Timestamp:O} after {latestBefore:O}");
        Assert.Contains($"\"carrierOccurredAt\":\"{occurredAt:yyyy-MM-ddTHH:mm:ss}", evt.Metadata);
    }

    [Fact]
    public async Task ApplyCarrierStatusAsync_AlreadyAtTarget_IsNoOp()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit);
        var eventsBefore = await dbContext.Set<ShipmentEvent>().CountAsync(e => e.ShipmentId == shipmentId);

        var result = await service.ApplyCarrierStatusAsync(shipmentId, ShipmentStatus.InInternationalTransit, DateTimeOffset.UtcNow, CarrierRef());

        Assert.Equal(CarrierStatusApplyResult.AlreadyAtStatus, result);
        Assert.Equal(eventsBefore, await dbContext.Set<ShipmentEvent>().CountAsync(e => e.ShipmentId == shipmentId));
    }

    [Fact]
    public async Task ApplyCarrierStatusAsync_BackwardsMove_ThrowsInvalidTransition()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.DeliveredAbroad);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() =>
            service.ApplyCarrierStatusAsync(shipmentId, ShipmentStatus.InInternationalTransit, DateTimeOffset.UtcNow, CarrierRef()));
    }

    [Fact]
    public async Task ApplyCarrierStatusAsync_UnknownShipment_ReturnsNotFound()
    {
        var (dbContext, organizationId, _) = await SeedAsync();
        await using var __ = dbContext;
        var service = CreateService(dbContext, organizationId);

        var result = await service.ApplyCarrierStatusAsync(Guid.NewGuid(), ShipmentStatus.InInternationalTransit, DateTimeOffset.UtcNow, CarrierRef());

        Assert.Equal(CarrierStatusApplyResult.NotFound, result);
    }

    [Fact]
    public async Task OverrideInternationalStatusAsync_FromCustomsHold_AppliesWithStaffUserAndReason()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.CustomsHold);
        var staffUserId = Guid.NewGuid();

        var result = await service.OverrideInternationalStatusAsync(shipmentId, ShipmentStatus.InInternationalTransit, "  Released by customs officer  ", ActingUser(staffUserId));

        Assert.Equal(ShipmentStatus.InInternationalTransit, result!.CurrentStatus);
        var evt = await dbContext.Set<ShipmentEvent>().SingleAsync(e => e.ShipmentId == shipmentId && e.EventType == ShipmentEventType.InInternationalTransit);
        Assert.Equal(staffUserId, evt.UserId);
        Assert.Equal("Released by customs officer", evt.Notes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OverrideInternationalStatusAsync_BlankReason_ThrowsValidation(string reason)
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.CustomsHold);

        var ex = await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            service.OverrideInternationalStatusAsync(shipmentId, ShipmentStatus.InInternationalTransit, reason, ActingUser(Guid.NewGuid())));
        Assert.IsNotType<ShipmentInvalidTransitionException>(ex);
    }

    [Fact]
    public async Task OverrideInternationalStatusAsync_NotAnExceptionExit_ThrowsInvalidTransition()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() =>
            service.OverrideInternationalStatusAsync(shipmentId, ShipmentStatus.DeliveredAbroad, "Carrier confirmed by phone", ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task HandToCarrierAsync_FromCustomsPrepared_ClearsCustodyAndRecordsCarrier()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var branchId = Guid.NewGuid();
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.CustomsPrepared, branchId, branchId, branchId);
        var accountId = Guid.NewGuid();

        var result = await service.HandToCarrierAsync(shipmentId, new CarrierHandoffReference(accountId, "DHL123"), ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.HandedToCarrier, result!.CurrentStatus);
        Assert.Null(result.CurrentBranchId);
        Assert.Null(result.CurrentCustodianUserId);
        var evt = await dbContext.Set<ShipmentEvent>().SingleAsync(e => e.ShipmentId == shipmentId && e.EventType == ShipmentEventType.HandedToCarrier);
        Assert.Equal(branchId, evt.BranchId);
        Assert.Contains("DHL123", evt.Metadata);
        Assert.Contains(accountId.ToString(), evt.Metadata);
    }

    [Fact]
    public async Task HandToCarrierAsync_WrongState_ThrowsInvalidTransition()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var branchId = Guid.NewGuid();
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.Sorted, branchId, branchId, branchId);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() =>
            service.HandToCarrierAsync(shipmentId, new CarrierHandoffReference(Guid.NewGuid(), "DHL123"), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task HandToCarrierAsync_InboundShipment_ThrowsValidation()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var branchId = Guid.NewGuid();
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Inbound, ShipmentStatus.CustomsPrepared, branchId, branchId, branchId);

        var ex = await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            service.HandToCarrierAsync(shipmentId, new CarrierHandoffReference(Guid.NewGuid(), "DHL123"), ActingUser(Guid.NewGuid())));
        Assert.IsNotType<ShipmentInvalidTransitionException>(ex);
    }

    [Fact]
    public async Task HandToCarrierAsync_UserWithoutAccessToCurrentBranch_ThrowsValidation()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var branchId = Guid.NewGuid();
        var shipmentId = await CreateAtStatusAsync(dbContext, CreateService(dbContext, organizationId), serviceTypeId,
            ShipmentDirection.Outbound, ShipmentStatus.CustomsPrepared, branchId, branchId, branchId);
        var restricted = CreateService(dbContext, organizationId, branchAuthorizationChecker: new SpecificBranchesAuthorizationChecker(Guid.NewGuid()));

        await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            restricted.HandToCarrierAsync(shipmentId, new CarrierHandoffReference(Guid.NewGuid(), "DHL123"), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ReceiveFromCarrierAsync_OriginDiffersFromDestination_StopsAtReceivedFromCarrier()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var gatewayBranchId = Guid.NewGuid();
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Inbound, ShipmentStatus.CustomsClearance, gatewayBranchId, Guid.NewGuid(), null);
        var userId = Guid.NewGuid();

        var result = await service.ReceiveFromCarrierAsync(shipmentId, ActingUser(userId));

        Assert.Equal(ShipmentStatus.ReceivedFromCarrier, result!.CurrentStatus);
        Assert.Equal(gatewayBranchId, result.CurrentBranchId);
        Assert.Equal(userId, result.CurrentCustodianUserId);
        var evt = await dbContext.Set<ShipmentEvent>().SingleAsync(e => e.ShipmentId == shipmentId && e.EventType == ShipmentEventType.ReceivedFromCarrier);
        Assert.Equal(gatewayBranchId, evt.BranchId);
    }

    [Fact]
    public async Task ReceiveFromCarrierAsync_OriginEqualsDestination_RecordsTwoEventsAndArrives()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var branchId = Guid.NewGuid();
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Inbound, ShipmentStatus.InInternationalTransit, branchId, branchId, null);

        var result = await service.ReceiveFromCarrierAsync(shipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.AtDestinationBranch, result!.CurrentStatus);
        var events = await dbContext.Set<ShipmentEvent>().Where(e => e.ShipmentId == shipmentId).OrderBy(e => e.Timestamp).ToListAsync();
        Assert.Contains(events, e => e.EventType == ShipmentEventType.ReceivedFromCarrier && e.NewStatus == ShipmentStatus.ReceivedFromCarrier);
        Assert.Contains(events, e => e.EventType == ShipmentEventType.ReceivedAtDestination
            && e.PreviousStatus == ShipmentStatus.ReceivedFromCarrier && e.NewStatus == ShipmentStatus.AtDestinationBranch);
    }

    [Fact]
    public async Task ReceiveFromCarrierAsync_UserWithoutAccessToGatewayBranch_ThrowsValidation()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var shipmentId = await CreateAtStatusAsync(dbContext, CreateService(dbContext, organizationId), serviceTypeId,
            ShipmentDirection.Inbound, ShipmentStatus.CustomsClearance);
        var restricted = CreateService(dbContext, organizationId, branchAuthorizationChecker: new SpecificBranchesAuthorizationChecker(Guid.NewGuid()));

        await Assert.ThrowsAsync<ShipmentValidationException>(() => restricted.ReceiveFromCarrierAsync(shipmentId, ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ReceiveFromCarrierAsync_FromCreated_ThrowsInvalidTransition()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Inbound, ShipmentStatus.Created);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => service.ReceiveFromCarrierAsync(shipmentId, ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task SortAsync_InboundReceivedFromCarrier_Sorts()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var branchId = Guid.NewGuid();
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Inbound, ShipmentStatus.ReceivedFromCarrier, branchId, Guid.NewGuid(), branchId);

        var result = await service.SortAsync(shipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.Sorted, result!.CurrentStatus);
    }

    [Fact]
    public async Task SortAsync_OutboundAtReceivedFromCarrier_ThrowsInvalidTransition()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.ReceivedFromCarrier);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => service.SortAsync(shipmentId, ActingUser(Guid.NewGuid())));
    }
}
