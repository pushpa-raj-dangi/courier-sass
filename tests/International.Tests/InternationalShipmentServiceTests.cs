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

internal class RecordingAuditLogger : IAuditLogger
{
    public List<AuditEntry> Entries { get; } = new();
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

internal class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

// Records the last CreateShipmentShellAsync call and returns a fixed ShipmentResponse — the
// gateway's own creation logic is Shipments.Tests' responsibility (Task 6); this fake only proves
// InternationalShipmentService calls it with the right arguments and handles the response.
internal class RecordingGateway : IShipmentInternationalGateway
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

    public bool CustomsPreparedCalled { get; private set; }
    public Task<ShipmentResponse?> TransitionToCustomsPreparedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        CustomsPreparedCalled = true;
        return Task.FromResult<ShipmentResponse?>(new ShipmentResponse(
            shipmentId, "AWB-INTL-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, null, ShipmentStatus.CustomsPrepared, Guid.NewGuid(),
            1m, null, null, null, 5000m, 0m, false, 5000m, 0m, 0m, 5000m,
            "R", "1", "L", "S", "2", "L", new List<ShipmentItemResponse>(), DateTimeOffset.UtcNow));
    }

    // I4 — replaced with recording versions in Task 5 and Task 13; not exercised by this file's
    // tests today.
    public Task<CarrierStatusApplyResult> ApplyCarrierStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, DateTimeOffset occurredAt, CarrierEventReference reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> OverrideInternationalStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, string reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal class FixedItemLookup : IShipmentItemLookupService
{
    private readonly List<ShipmentItemRecord> _items;
    public FixedItemLookup(List<ShipmentItemRecord> items) => _items = items;
    public Task<IReadOnlyList<ShipmentItemRecord>> GetItemsAsync(Guid shipmentId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ShipmentItemRecord>>(_items);
}

internal class FixedCountryLookup : ICountryLookupService
{
    private readonly HashSet<string> _serviced;
    private readonly HashSet<string> _existing;
    public FixedCountryLookup(params string[] serviced)
    {
        _serviced = serviced.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _existing = new HashSet<string>(new[] { "NP", "GB", "IN", "US", "TV" }.Concat(serviced), StringComparer.OrdinalIgnoreCase);
    }
    public Task<bool> IsServicedAsync(string isoCode, CancellationToken cancellationToken = default) => Task.FromResult(_serviced.Contains(isoCode));
    public Task<bool> ExistsAsync(string isoCode, CancellationToken cancellationToken = default) => Task.FromResult(_existing.Contains(isoCode));
}

// Only the two read methods International.Application uses are implemented; every transition
// method is out of scope for this module's tests.
internal class FixedShipmentTransitionService : IShipmentTransitionService
{
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Sorted;
    public Guid OriginBranchId { get; set; } = Guid.NewGuid();
    public Guid DestinationBranchId { get; set; } = Guid.NewGuid();
    public Guid? CurrentBranchId { get; set; }
    public bool Exists { get; set; } = true;
    public ShipmentDirection Direction { get; set; } = ShipmentDirection.Outbound;

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Exists ? new ShipmentScanInfo(shipmentId, Guid.NewGuid(), "AWB-INTL-1", Status, CurrentBranchId ?? OriginBranchId, OriginBranchId, DestinationBranchId, null, 0m, Direction) : null);
    public Task<ShipmentResponse?> GetResponseAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Exists ? new ShipmentResponse(
            shipmentId, "AWB-INTL-1", Guid.NewGuid(), OriginBranchId, DestinationBranchId, CurrentBranchId ?? OriginBranchId, null, null, Status, Guid.NewGuid(),
            1m, null, null, null, 5000m, 0m, false, 5000m, 0m, 0m, 5000m,
            "R", "1", "L", "S", "2", "L", new List<ShipmentItemResponse>(), DateTimeOffset.UtcNow) : null);

    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal class FixedBranchAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid>? _branchIds;
    // null == BranchScope.AllBranches (no filter), matching IBranchAuthorizationChecker's contract.
    public FixedBranchAuthorizationChecker(IReadOnlySet<Guid>? branchIds) => _branchIds = branchIds;
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) => Task.FromResult(_branchIds);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(_branchIds is null || _branchIds.Contains(branchId));
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

    private sealed class Harness
    {
        public required TestDbContext DbContext { get; init; }
        public required Guid OrganizationId { get; init; }
        public required RecordingGateway Gateway { get; init; }
        public required RecordingAuditLogger AuditLogger { get; init; }
        public required FixedShipmentTransitionService Shipments { get; init; }
        public required Guid ItemId { get; init; }
        public required InternationalShipmentService Service { get; init; }
        public required InternationalShipmentQueryService QueryService { get; init; }
    }

    private static Harness CreateHarness(IReadOnlySet<Guid>? accessibleBranchIds = null, params string[] servicedCountries)
    {
        var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var gateway = new RecordingGateway();
        var auditLogger = new RecordingAuditLogger();
        var shipments = new FixedShipmentTransitionService();
        var itemId = Guid.NewGuid();
        var itemLookup = new FixedItemLookup(new() { new ShipmentItemRecord(itemId, "Sample goods", 1, 5000m) });
        var branchChecker = new FixedBranchAuthorizationChecker(accessibleBranchIds);
        var service = new InternationalShipmentService(
            dbContext, new FixedTenantProvider(organizationId), auditLogger, gateway,
            new FixedCountryLookup(servicedCountries.Length == 0 ? new[] { "GB" } : servicedCountries), itemLookup, shipments, branchChecker);
        var queryService = new InternationalShipmentQueryService(dbContext, shipments, branchChecker);
        return new Harness
        {
            DbContext = dbContext, OrganizationId = organizationId, Gateway = gateway, AuditLogger = auditLogger,
            Shipments = shipments, ItemId = itemId, Service = service, QueryService = queryService
        };
    }

    private static UpdateCustomsRequest ValidCustoms(Guid itemId) => new("Sample goods for testing", "NP", "Sample", "Ddu", null,
        new List<CustomsItemRequest> { new(itemId, "HS1234", 5000m, "NPR") });

    [Fact]
    public async Task CreateAsync_ServicedCountry_CallsGatewayAndPersistsInternationalLeg()
    {
        var h = CreateHarness();

        var result = await h.Service.CreateAsync(ValidRequest(), ActingUser());

        Assert.NotNull(h.Gateway.LastRequest);
        Assert.Equal("GB", h.Gateway.LastRequest!.RecipientCountryCode);
        Assert.Equal(h.Gateway.ShipmentId, result.Shipment.Id);
        Assert.False(result.Leg.CustomsCompleted);
        Assert.True(result.Leg.IsProvisionalPricing);
        Assert.Equal(5000m, result.Leg.ProvisionalTotalChargeAmount);
        Assert.Empty(result.Leg.CustomsItems);

        var stored = await h.DbContext.Set<InternationalLeg>().SingleAsync(l => l.ShipmentId == h.Gateway.ShipmentId);
        Assert.Equal(h.OrganizationId, stored.OrganizationId);
        Assert.Null(stored.CustomsCompletedAt);
    }

    [Fact]
    public async Task CreateAsync_UnservicedCountry_ThrowsAndNeverCallsGateway()
    {
        var h = CreateHarness(null, "US");

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() => h.Service.CreateAsync(ValidRequest(), ActingUser()));

        Assert.Null(h.Gateway.LastRequest);
    }

    // C1: every create-time validation runs before the gateway commits anything — an invalid
    // input must never reach CreateShipmentShellAsync (which is what used to orphan a Shipment).
    public static TheoryData<string, CreateInternationalShipmentRequest> InvalidCreateRequests() => new()
    {
        { "invalid currency", ValidRequest() with { ProvisionalTotalChargeCurrency = "RUPEES" } },
        { "numeric currency", ValidRequest() with { ProvisionalTotalChargeCurrency = "123" } },
        { "negative charge", ValidRequest() with { ProvisionalTotalChargeAmount = -1m } },
        { "zero weight", ValidRequest() with { WeightKg = 0m } },
        { "domestic direction", ValidRequest() with { Direction = ShipmentDirection.Domestic } },
        { "undefined direction", ValidRequest() with { Direction = (ShipmentDirection)42 } },
        { "null items", ValidRequest() with { Items = null! } },
        { "no items", ValidRequest() with { Items = new List<CreateShipmentItemRequest>() } },
        { "null item element", ValidRequest() with { Items = new List<CreateShipmentItemRequest> { null! } } },
        { "zero item quantity", ValidRequest() with { Items = new List<CreateShipmentItemRequest> { new("Books", 0, 100m) } } },
        { "negative item quantity", ValidRequest() with { Items = new List<CreateShipmentItemRequest> { new("Books", -1, 100m) } } },
        { "negative item unit value", ValidRequest() with { Items = new List<CreateShipmentItemRequest> { new("Books", 1, -0.01m) } } },
        { "foreign region too long", ValidRequest() with { RecipientRegion = new string('R', 201) } },
        { "foreign postal code too long", ValidRequest() with { RecipientPostalCode = new string('9', 21) } },
        { "foreign city too long", ValidRequest() with { RecipientCity = new string('C', 201) } },
    };

    [Theory]
    [MemberData(nameof(InvalidCreateRequests))]
    public async Task CreateAsync_InvalidInput_ThrowsValidationAndNeverCallsGateway(string label, CreateInternationalShipmentRequest request)
    {
        var h = CreateHarness();

        var ex = await Assert.ThrowsAsync<InternationalShipmentValidationException>(() => h.Service.CreateAsync(request, ActingUser()));

        Assert.False(string.IsNullOrEmpty(ex.Message), label);
        Assert.Null(h.Gateway.LastRequest);
        Assert.Empty(await h.DbContext.Set<InternationalLeg>().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_LowercaseCurrency_IsNormalized()
    {
        var h = CreateHarness();

        var result = await h.Service.CreateAsync(ValidRequest() with { ProvisionalTotalChargeCurrency = "npr" }, ActingUser());

        Assert.Equal("NPR", result.Leg.ProvisionalTotalChargeCurrency);
    }

    [Fact]
    public async Task UpdateCustomsAsync_ValidItems_CompletesDeclarationAndReturnsItems()
    {
        var h = CreateHarness();
        var created = await h.Service.CreateAsync(ValidRequest(), ActingUser());

        var result = await h.Service.UpdateCustomsAsync(created.Shipment.Id, ValidCustoms(h.ItemId), ActingUser());

        Assert.NotNull(result);
        Assert.True(result!.CustomsCompleted);
        Assert.Equal("Sample goods for testing", result.ContentsDescription);
        var item = Assert.Single(result.CustomsItems);
        Assert.Equal(h.ItemId, item.ShipmentItemId);
        Assert.Equal("HS1234", item.HsCode);

        var legId = (await h.DbContext.Set<InternationalLeg>().SingleAsync(l => l.ShipmentId == created.Shipment.Id)).Id;
        var storedItems = await h.DbContext.Set<CustomsItem>().Where(i => i.InternationalLegId == legId).ToListAsync();
        Assert.Single(storedItems);
        Assert.Equal("HS1234", storedItems[0].HsCode);
    }

    [Fact]
    public async Task UpdateCustomsAsync_ItemNotBelongingToShipment_Throws()
    {
        var h = CreateHarness();
        var created = await h.Service.CreateAsync(ValidRequest(), ActingUser());

        var request = new UpdateCustomsRequest("Desc", "NP", "Sample", "Ddu", null,
            new List<CustomsItemRequest> { new(Guid.NewGuid(), "HS1234", 5000m, "NPR") });

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() => h.Service.UpdateCustomsAsync(created.Shipment.Id, request, ActingUser()));
    }

    public static TheoryData<string, Func<Guid, UpdateCustomsRequest>> InvalidCustomsRequests() => new()
    {
        { "null items", id => ValidCustoms(id) with { Items = null! } },
        { "invalid currency", id => ValidCustoms(id) with { Items = new() { new(id, "HS1234", 5000m, "RUPEES") } } },
        { "negative customs value", id => ValidCustoms(id) with { Items = new() { new(id, "HS1234", -1m, "NPR") } } },
        { "hs code too long", id => ValidCustoms(id) with { Items = new() { new(id, new string('1', 21), 5000m, "NPR") } } },
        { "blank hs code", id => ValidCustoms(id) with { Items = new() { new(id, " ", 5000m, "NPR") } } },
        { "duplicate item", id => ValidCustoms(id) with { Items = new() { new(id, "HS1", 1m, "NPR"), new(id, "HS2", 1m, "NPR") } } },
        { "country of origin not two letters", id => ValidCustoms(id) with { CountryOfOrigin = "NPL" } },
        { "country of origin unknown", id => ValidCustoms(id) with { CountryOfOrigin = "ZZ" } },
        { "contents too long", id => ValidCustoms(id) with { ContentsDescription = new string('c', 1001) } },
        { "blank contents", id => ValidCustoms(id) with { ContentsDescription = "  " } },
        { "licence too long", id => ValidCustoms(id) with { ExportLicenceReference = new string('L', 201) } },
    };

    [Theory]
    [MemberData(nameof(InvalidCustomsRequests))]
    public async Task UpdateCustomsAsync_InvalidInput_ThrowsValidationAndLeavesLegUntouched(string label, Func<Guid, UpdateCustomsRequest> build)
    {
        var h = CreateHarness();
        var created = await h.Service.CreateAsync(ValidRequest(), ActingUser());

        var ex = await Assert.ThrowsAsync<InternationalShipmentValidationException>(() => h.Service.UpdateCustomsAsync(created.Shipment.Id, build(h.ItemId), ActingUser()));

        Assert.False(string.IsNullOrEmpty(ex.Message), label);
        Assert.Null((await h.DbContext.Set<InternationalLeg>().SingleAsync()).CustomsCompletedAt);
        Assert.Empty(await h.DbContext.Set<CustomsItem>().ToListAsync());
    }

    [Fact]
    public async Task UpdateCustomsAsync_SubsetOfShipmentItems_Throws()
    {
        var dbContext = CreateContext();
        var itemA = Guid.NewGuid();
        var itemB = Guid.NewGuid();
        var itemLookup = new FixedItemLookup(new() { new(itemA, "A", 1, 1m), new(itemB, "B", 1, 1m) });
        var service = new InternationalShipmentService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new RecordingAuditLogger(), new RecordingGateway(),
            new FixedCountryLookup("GB"), itemLookup, new FixedShipmentTransitionService(), new FixedBranchAuthorizationChecker(null));
        var created = await service.CreateAsync(ValidRequest(), ActingUser());

        var ex = await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            service.UpdateCustomsAsync(created.Shipment.Id, ValidCustoms(itemA), ActingUser()));
        Assert.Contains("every shipment item", ex.Message);
    }

    // I5 ruling: the declaration is frozen once the shipment reaches CustomsPrepared.
    [Theory]
    [InlineData(ShipmentStatus.CustomsPrepared)]
    [InlineData(ShipmentStatus.Cancelled)]
    public async Task UpdateCustomsAsync_AfterCustomsPrepared_Throws(ShipmentStatus status)
    {
        var h = CreateHarness();
        var created = await h.Service.CreateAsync(ValidRequest(), ActingUser());
        await h.Service.UpdateCustomsAsync(created.Shipment.Id, ValidCustoms(h.ItemId), ActingUser());
        h.Shipments.Status = status;

        var ex = await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.UpdateCustomsAsync(created.Shipment.Id, ValidCustoms(h.ItemId) with { ContentsDescription = "Changed" }, ActingUser()));

        Assert.Contains("no longer be edited", ex.Message);
        Assert.Equal("Sample goods for testing", (await h.DbContext.Set<InternationalLeg>().SingleAsync()).ContentsDescription);
    }

    [Fact]
    public async Task UpdateCustomsAsync_AuditsBeforeAndAfterDeclaration()
    {
        var h = CreateHarness();
        var created = await h.Service.CreateAsync(ValidRequest(), ActingUser());
        await h.Service.UpdateCustomsAsync(created.Shipment.Id, ValidCustoms(h.ItemId), ActingUser());

        await h.Service.UpdateCustomsAsync(created.Shipment.Id, ValidCustoms(h.ItemId) with { ContentsDescription = "Second version" }, ActingUser());

        var entry = h.AuditLogger.Entries.Last(e => e.Action == "international_leg.customs_updated");
        Assert.NotNull(entry.Before);
        Assert.NotNull(entry.After);
        Assert.Contains("Sample goods for testing", entry.Before);
        Assert.Contains("HS1234", entry.Before);
        Assert.Contains("Second version", entry.After);
    }

    [Fact]
    public async Task UpdateCustomsAsync_ShipmentOutsideCallersBranches_ReturnsNull()
    {
        var h = CreateHarness(new HashSet<Guid> { Guid.NewGuid() });
        var created = await h.Service.CreateAsync(ValidRequest(), ActingUser());

        var result = await h.Service.UpdateCustomsAsync(created.Shipment.Id, ValidCustoms(h.ItemId), ActingUser());

        Assert.Null(result);
        Assert.Null((await h.DbContext.Set<InternationalLeg>().SingleAsync()).CustomsCompletedAt);
    }

    [Fact]
    public async Task UpdateCustomsAsync_ShipmentAtCallersDestinationBranch_Succeeds()
    {
        var destinationBranchId = Guid.NewGuid();
        var h = CreateHarness(new HashSet<Guid> { destinationBranchId });
        h.Shipments.DestinationBranchId = destinationBranchId;
        var created = await h.Service.CreateAsync(ValidRequest(), ActingUser());

        var result = await h.Service.UpdateCustomsAsync(created.Shipment.Id, ValidCustoms(h.ItemId), ActingUser());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task PrepareCustomsAsync_BeforeCustomsCompleted_Throws()
    {
        var h = CreateHarness();
        var created = await h.Service.CreateAsync(ValidRequest(), ActingUser());

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() => h.Service.PrepareCustomsAsync(created.Shipment.Id, ActingUser()));
        Assert.False(h.Gateway.CustomsPreparedCalled);
    }

    [Fact]
    public async Task PrepareCustomsAsync_AfterCustomsCompleted_CallsGateway()
    {
        var h = CreateHarness();
        var created = await h.Service.CreateAsync(ValidRequest(), ActingUser());
        await h.Service.UpdateCustomsAsync(created.Shipment.Id, ValidCustoms(h.ItemId), ActingUser());

        var result = await h.Service.PrepareCustomsAsync(created.Shipment.Id, ActingUser());

        Assert.NotNull(result);
        Assert.True(h.Gateway.CustomsPreparedCalled);
    }

    [Fact]
    public async Task PrepareCustomsAsync_ShipmentOutsideCallersBranches_ReturnsNullWithoutCallingGateway()
    {
        var h = CreateHarness(new HashSet<Guid> { Guid.NewGuid() });
        var created = await h.Service.CreateAsync(ValidRequest(), ActingUser());

        var result = await h.Service.PrepareCustomsAsync(created.Shipment.Id, ActingUser());

        Assert.Null(result);
        Assert.False(h.Gateway.CustomsPreparedCalled);
    }

    [Fact]
    public async Task GetAsync_ReturnsCustomsItems()
    {
        var h = CreateHarness();
        var created = await h.Service.CreateAsync(ValidRequest(), ActingUser());
        await h.Service.UpdateCustomsAsync(created.Shipment.Id, ValidCustoms(h.ItemId), ActingUser());

        var detail = await h.QueryService.GetAsync(created.Shipment.Id, ActingUser());

        Assert.NotNull(detail);
        var item = Assert.Single(detail!.Leg.CustomsItems);
        Assert.Equal(h.ItemId, item.ShipmentItemId);
        Assert.Equal("HS1234", item.HsCode);
        Assert.Equal(5000m, item.CustomsValueAmount);
        Assert.Equal("NPR", item.CustomsValueCurrency);
    }

    [Fact]
    public async Task GetAsync_ShipmentOutsideCallersBranches_ReturnsNull()
    {
        var h = CreateHarness(new HashSet<Guid> { Guid.NewGuid() });
        var created = await h.Service.CreateAsync(ValidRequest(), ActingUser());

        Assert.Null(await h.QueryService.GetAsync(created.Shipment.Id, ActingUser()));
    }
}
