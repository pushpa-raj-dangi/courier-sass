using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Events.Application;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Cod.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class UnusedBranchLookup : IBranchLookupService
{
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class UnusedMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class AwbLookup : IShipmentAwbLookupService
{
    public Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(shipmentIds.ToDictionary(id => id, id => "AWB-" + id.ToString("N")[..6]));
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal class StubDeliveryLookup : IShipmentDeliveryLookupService
{
    public HashSet<(Guid ShipmentId, Guid MerchantId)> Owned { get; } = new();
    public int Delivered { get; set; }
    public (DateTimeOffset From, DateTimeOffset To)? LastRange { get; private set; }

    public Task<int> CountDeliveredAsync(Guid merchantId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        LastRange = (fromUtc, toUtc);
        return Task.FromResult(Delivered);
    }

    public Task<bool> BelongsToMerchantAsync(Guid shipmentId, Guid merchantId, CancellationToken ct = default) =>
        Task.FromResult(Owned.Contains((shipmentId, merchantId)));
}

public class CodServiceMerchantPortalTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-05T06:00:00Z");

    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantSettlementItemConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId, StubDeliveryLookup? delivery = null) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new UnusedBranchLookup(),
        new AllBranchesAuthorizationChecker(), new UnusedMerchantLookup(), new AwbLookup(), new NoopOutboxWriter(),
        new FixedTimeZoneLookup(), delivery ?? new StubDeliveryLookup());

    private static ClaimsPrincipal MerchantUser(Guid merchantId) =>
        new(new ClaimsIdentity(new[] { new Claim("merchant_id", merchantId.ToString()) }, "test"));

    private static ClaimsPrincipal StaffUser() =>
        new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    private static async Task<CODCollection> SeedCollectionAsync(
        TestDbContext dbContext, Guid organizationId, Guid merchantId, CodCollectionStatus status, decimal amount,
        decimal totalCharge = 150m, DateTimeOffset? collectedAt = null, Guid? itemId = null, Guid? shipmentId = null)
    {
        var collected = status != CodCollectionStatus.CodExpected;
        var collection = new CODCollection
        {
            Id = Guid.NewGuid(), ShipmentId = shipmentId ?? Guid.NewGuid(), OrganizationId = organizationId, MerchantId = merchantId,
            BranchId = Guid.NewGuid(), ExpectedAmount = amount, ShipmentTotalCharge = totalCharge,
            CollectedAmount = collected ? amount : null, CollectedAt = collected ? collectedAt ?? Now.AddHours(-1) : null,
            Status = status, MerchantSettlementItemId = itemId
        };
        dbContext.Set<CODCollection>().Add(collection);
        await dbContext.SaveChangesAsync();
        return collection;
    }

    private static async Task<MerchantSettlement> SeedSettlementAsync(
        TestDbContext dbContext, Guid organizationId, Guid merchantId, MerchantSettlementStatus status, decimal total, DateTimeOffset? settledAt = null)
    {
        var settlement = new MerchantSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = merchantId,
            PeriodFrom = Now.AddDays(-7), PeriodTo = Now, TotalPayable = total, Status = status, SettledAt = settledAt
        };
        dbContext.Set<MerchantSettlement>().Add(settlement);
        await dbContext.SaveChangesAsync();
        return settlement;
    }

    private static async Task<MerchantSettlementItem> SeedItemAsync(TestDbContext dbContext, MerchantSettlement settlement, Guid shipmentId)
    {
        var item = new MerchantSettlementItem
        {
            Id = Guid.NewGuid(), MerchantSettlementId = settlement.Id, ShipmentId = shipmentId, CODAmount = 1000m, CourierFee = 150m, NetPayable = 850m
        };
        dbContext.Set<MerchantSettlementItem>().Add(item);
        await dbContext.SaveChangesAsync();
        return item;
    }

    [Fact]
    public async Task Summary_CollectedNotSettled_SumsOnlyOwnCollectedStatesWithoutItem()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedCollectionAsync(db, org, a, CodCollectionStatus.CodCollected, 1000m);
        await SeedCollectionAsync(db, org, a, CodCollectionStatus.FinanceVerified, 500m);
        await SeedCollectionAsync(db, org, a, CodCollectionStatus.CodExpected, 700m);
        var settlement = await SeedSettlementAsync(db, org, a, MerchantSettlementStatus.Created, 850m);
        var item = await SeedItemAsync(db, settlement, Guid.NewGuid());
        await SeedCollectionAsync(db, org, a, CodCollectionStatus.MerchantSettlementCreated, 1000m, itemId: item.Id);
        await SeedCollectionAsync(db, org, b, CodCollectionStatus.CodCollected, 9999m);

        var summary = await CreateService(db, org).GetMerchantCodSummaryAsync(MerchantUser(a), Now);

        Assert.Equal(new MoneyBucket(1500m, 2), summary.CollectedNotSettled);
    }

    [Fact]
    public async Task Summary_InSettlement_SumsCreatedSettlementsOfCaller()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var a = Guid.NewGuid();
        await SeedSettlementAsync(db, org, a, MerchantSettlementStatus.Created, 4820m);
        await SeedSettlementAsync(db, org, Guid.NewGuid(), MerchantSettlementStatus.Created, 111m);
        await SeedSettlementAsync(db, org, a, MerchantSettlementStatus.Settled, 999m, Now.AddDays(-1));

        var summary = await CreateService(db, org).GetMerchantCodSummaryAsync(MerchantUser(a), Now);

        Assert.Equal(new MoneyBucket(4820m, 1), summary.InSettlement);
    }

    [Fact]
    public async Task Summary_SettledThisMonth_UsesOrgLocalMonthBoundary()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var a = Guid.NewGuid();
        // 2026-09-30T18:20Z is 00:05 NPT on 1 Oct (in this month); 18:10Z is 23:55 NPT on 30 Sep (last month).
        var inMonth = DateTimeOffset.Parse("2026-09-30T18:20:00Z");
        await SeedSettlementAsync(db, org, a, MerchantSettlementStatus.Settled, 300m, inMonth);
        await SeedSettlementAsync(db, org, a, MerchantSettlementStatus.Settled, 200m, DateTimeOffset.Parse("2026-09-30T18:10:00Z"));

        var summary = await CreateService(db, org).GetMerchantCodSummaryAsync(MerchantUser(a), Now);

        Assert.Equal(300m, summary.SettledThisMonth.Amount);
        Assert.Equal(1, summary.SettledThisMonth.Payouts);
        Assert.Equal(inMonth, summary.SettledThisMonth.LastSettledAt);
    }

    [Fact]
    public async Task Summary_DeliveredToday_CountsFromLookupAndSumsTodaysCollections()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var a = Guid.NewGuid();
        // now = 2026-10-05T06:00Z = 11:45 NPT on 5 Oct; NPT day starts 2026-10-04T18:15Z.
        await SeedCollectionAsync(db, org, a, CodCollectionStatus.CodCollected, 400m, collectedAt: DateTimeOffset.Parse("2026-10-04T18:05:00Z")); // 23:50 NPT yesterday
        await SeedCollectionAsync(db, org, a, CodCollectionStatus.CodCollected, 250m, collectedAt: DateTimeOffset.Parse("2026-10-04T18:25:00Z")); // 00:10 NPT today
        var delivery = new StubDeliveryLookup { Delivered = 3 };

        var summary = await CreateService(db, org, delivery).GetMerchantCodSummaryAsync(MerchantUser(a), Now);

        Assert.Equal(new DeliveredTodayBucket(3, 250m), summary.DeliveredToday);
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T18:15:00Z"), delivery.LastRange!.Value.From);
        Assert.Equal(DateTimeOffset.Parse("2026-10-05T18:15:00Z"), delivery.LastRange!.Value.To);
    }

    [Fact]
    public async Task Summary_OtherOrganization_IsExcluded()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var a = Guid.NewGuid();
        await SeedCollectionAsync(db, Guid.NewGuid(), a, CodCollectionStatus.CodCollected, 1000m);
        await SeedSettlementAsync(db, Guid.NewGuid(), a, MerchantSettlementStatus.Created, 500m);

        var summary = await CreateService(db, org).GetMerchantCodSummaryAsync(MerchantUser(a), Now);

        Assert.Equal(new MoneyBucket(0m, 0), summary.CollectedNotSettled);
        Assert.Equal(new MoneyBucket(0m, 0), summary.InSettlement);
    }

    [Fact]
    public async Task Awaiting_ListsOwnRowsNewestFirstWithNet()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var a = Guid.NewGuid();
        var older = await SeedCollectionAsync(db, org, a, CodCollectionStatus.CodCollected, 1000m, 150m, Now.AddDays(-2));
        var newer = await SeedCollectionAsync(db, org, a, CodCollectionStatus.BranchReconciled, 2500m, 150m, Now.AddHours(-1));
        await SeedCollectionAsync(db, org, Guid.NewGuid(), CodCollectionStatus.CodCollected, 9999m);

        var page = await CreateService(db, org).ListMerchantAwaitingAsync(null, null, MerchantUser(a));

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new[] { newer.ShipmentId, older.ShipmentId }, page.Items.Select(i => i.ShipmentId).ToArray());
        Assert.Equal(2350m, page.Items[0].Net);
        Assert.Equal(150m, page.Items[0].CourierFee);
        Assert.Equal(2500m, page.Items[0].CollectedAmount);
        Assert.StartsWith("AWB-", page.Items[0].Awb);
    }

    [Fact]
    public async Task PortalMethods_WithoutMerchantClaim_Throw()
    {
        await using var db = CreateContext();
        var service = CreateService(db, Guid.NewGuid());

        var ex1 = await Assert.ThrowsAsync<CodValidationException>(() => service.GetMerchantCodSummaryAsync(StaffUser(), Now));
        var ex2 = await Assert.ThrowsAsync<CodValidationException>(() => service.ListMerchantAwaitingAsync(null, null, StaffUser()));
        var ex3 = await Assert.ThrowsAsync<CodValidationException>(() => service.GetMerchantShipmentCodStatusAsync(Guid.NewGuid(), StaffUser()));
        Assert.All(new[] { ex1, ex2, ex3 }, e => Assert.Equal("Merchant context required.", e.Message));
    }

    private static async Task<(CodService Service, Guid Merchant, Guid ShipmentId, TestDbContext Db, Guid Org)> StatusSetupAsync(CodCollectionStatus? status)
    {
        var db = CreateContext();
        var org = Guid.NewGuid();
        var merchant = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var delivery = new StubDeliveryLookup();
        delivery.Owned.Add((shipmentId, merchant));
        if (status is not null)
        {
            await SeedCollectionAsync(db, org, merchant, status.Value, 1000m, shipmentId: shipmentId);
        }
        return (CreateService(db, org, delivery), merchant, shipmentId, db, org);
    }

    [Fact]
    public async Task Status_CodExpected_IsNotCollected()
    {
        var (service, merchant, shipmentId, db, _) = await StatusSetupAsync(CodCollectionStatus.CodExpected);
        await using var _db = db;

        var result = await service.GetMerchantShipmentCodStatusAsync(shipmentId, MerchantUser(merchant));

        Assert.Equal(MerchantCodState.NotCollected, result!.State);
    }

    [Fact]
    public async Task Status_CollectedWithoutItem_IsAwaitingSettlement()
    {
        var (service, merchant, shipmentId, db, _) = await StatusSetupAsync(CodCollectionStatus.RiderDeposited);
        await using var _db = db;

        var result = await service.GetMerchantShipmentCodStatusAsync(shipmentId, MerchantUser(merchant));

        Assert.Equal(MerchantCodState.CollectedAwaitingSettlement, result!.State);
        Assert.Null(result.SettlementId);
    }

    [Fact]
    public async Task Status_ItemInCreatedSettlement_IsInSettlement()
    {
        var (service, merchant, shipmentId, db, org) = await StatusSetupAsync(null);
        await using var _db = db;
        var settlement = await SeedSettlementAsync(db, org, merchant, MerchantSettlementStatus.Created, 850m);
        var item = await SeedItemAsync(db, settlement, shipmentId);
        await SeedCollectionAsync(db, org, merchant, CodCollectionStatus.MerchantSettlementCreated, 1000m, itemId: item.Id, shipmentId: shipmentId);

        var result = await service.GetMerchantShipmentCodStatusAsync(shipmentId, MerchantUser(merchant));

        Assert.Equal(MerchantCodState.InSettlement, result!.State);
        Assert.Equal(settlement.Id, result.SettlementId);
        Assert.Null(result.SettledAt);
    }

    [Fact]
    public async Task Status_ItemInSettledSettlement_IsSettledWithPeriodAndDate()
    {
        var (service, merchant, shipmentId, db, org) = await StatusSetupAsync(null);
        await using var _db = db;
        var settledAt = Now.AddDays(-1);
        var settlement = await SeedSettlementAsync(db, org, merchant, MerchantSettlementStatus.Settled, 850m, settledAt);
        var item = await SeedItemAsync(db, settlement, shipmentId);
        await SeedCollectionAsync(db, org, merchant, CodCollectionStatus.Settled, 1000m, itemId: item.Id, shipmentId: shipmentId);

        var result = await service.GetMerchantShipmentCodStatusAsync(shipmentId, MerchantUser(merchant));

        Assert.Equal(MerchantCodState.Settled, result!.State);
        Assert.Equal(settledAt, result.SettledAt);
        Assert.Equal(settlement.PeriodFrom, result.PeriodFrom);
        Assert.Equal(settlement.PeriodTo, result.PeriodTo);
    }

    [Fact]
    public async Task Status_ShipmentOfAnotherMerchant_ReturnsNull()
    {
        var (service, _, shipmentId, db, _) = await StatusSetupAsync(CodCollectionStatus.CodCollected);
        await using var _db = db;

        var result = await service.GetMerchantShipmentCodStatusAsync(shipmentId, MerchantUser(Guid.NewGuid()));

        Assert.Null(result);
    }

    [Fact]
    public async Task Status_OwnShipmentWithoutCollection_IsPrepaid()
    {
        var (service, merchant, shipmentId, db, _) = await StatusSetupAsync(null);
        await using var _db = db;

        var result = await service.GetMerchantShipmentCodStatusAsync(shipmentId, MerchantUser(merchant));

        Assert.Equal(MerchantCodState.Prepaid, result!.State);
    }
}
