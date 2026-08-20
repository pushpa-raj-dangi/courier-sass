using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Pricing.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class AlwaysTrueGeographyLookup : IGeographyLookupService
{
    public Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public class RateCardServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ServiceTypeConfiguration());
            modelBuilder.ApplyConfiguration(new WeightSlabConfiguration());
            modelBuilder.ApplyConfiguration(new RateCardConfiguration());
            modelBuilder.ApplyConfiguration(new ZoneRateConfiguration());
            modelBuilder.ApplyConfiguration(new AdditionalChargeConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantRateCardConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", userId.ToString())
    }, "test"));

    private static RateCardService CreateService(TestDbContext dbContext, Guid organizationId) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup());

    [Fact]
    public async Task CreateRateCardAsync_FirstCardForOrg_IsVersionOneAndActive()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        var result = await service.CreateRateCardAsync(new CreateRateCardRequest("Default", DateTimeOffset.UtcNow), ActingUser(Guid.NewGuid()));

        Assert.Equal(1, result.Version);
        Assert.True(result.IsActive);
        Assert.Null(result.EffectiveTo);
    }

    [Fact]
    public async Task CreateRateCardAsync_EffectiveFromBeforePrevious_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());
        var actingUser = ActingUser(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        await service.CreateRateCardAsync(new CreateRateCardRequest("v1", now), actingUser);

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.CreateRateCardAsync(new CreateRateCardRequest("v2", now.AddDays(-1)), actingUser));
    }

    [Fact]
    public async Task CreateRateCardAsync_SecondCardForOrg_SupersedesFirstWithoutMutatingItsChildren()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());

        var first = await service.CreateRateCardAsync(new CreateRateCardRequest("v1", DateTimeOffset.UtcNow), actingUser);
        var serviceTypeId = dbContext.Set<ServiceType>().First().Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First().Id;
        var originZone = Guid.NewGuid();
        var destZone = Guid.NewGuid();
        var zoneRate = await service.AddZoneRateAsync(first.Id, new CreateZoneRateRequest(originZone, destZone, serviceTypeId, weightSlabId, 100m), actingUser);

        var second = await service.CreateRateCardAsync(new CreateRateCardRequest("v2", DateTimeOffset.UtcNow.AddDays(1)), actingUser);

        Assert.Equal(2, second.Version);
        Assert.True(second.IsActive);

        var firstReloaded = await service.GetRateCardAsync(first.Id);
        Assert.False(firstReloaded!.IsActive);
        Assert.NotNull(firstReloaded.EffectiveTo);

        var firstZoneRates = await service.ListZoneRatesAsync(first.Id);
        Assert.NotNull(firstZoneRates);
        Assert.Single(firstZoneRates);
        Assert.Equal(100m, firstZoneRates![0].BaseRate);
    }

    [Fact]
    public async Task AddZoneRateAsync_OnSupersededRateCard_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());
        var actingUser = ActingUser(Guid.NewGuid());

        var first = await service.CreateRateCardAsync(new CreateRateCardRequest("v1", DateTimeOffset.UtcNow), actingUser);
        await service.CreateRateCardAsync(new CreateRateCardRequest("v2", DateTimeOffset.UtcNow.AddDays(1)), actingUser);

        var serviceTypeId = dbContext.Set<ServiceType>().First().Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First().Id;

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.AddZoneRateAsync(first.Id, new CreateZoneRateRequest(Guid.NewGuid(), Guid.NewGuid(), serviceTypeId, weightSlabId, 50m), actingUser));
    }

    [Fact]
    public async Task AddZoneRateAsync_NegativeBaseRate_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());
        var actingUser = ActingUser(Guid.NewGuid());
        var rateCard = await service.CreateRateCardAsync(new CreateRateCardRequest("v1", DateTimeOffset.UtcNow), actingUser);
        var serviceTypeId = dbContext.Set<ServiceType>().First().Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First().Id;

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.AddZoneRateAsync(rateCard.Id, new CreateZoneRateRequest(Guid.NewGuid(), Guid.NewGuid(), serviceTypeId, weightSlabId, -1m), actingUser));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AddAdditionalChargeAsync_UndefinedEnumValue_Throws(bool invalidType, bool invalidCalculationType)
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());
        var actingUser = ActingUser(Guid.NewGuid());
        var rateCard = await service.CreateRateCardAsync(new CreateRateCardRequest("v1", DateTimeOffset.UtcNow), actingUser);

        var type = invalidType ? (AdditionalChargeType)999 : AdditionalChargeType.Fuel;
        var calculationType = invalidCalculationType ? (ChargeCalculationType)999 : ChargeCalculationType.Flat;

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.AddAdditionalChargeAsync(rateCard.Id, new CreateAdditionalChargeRequest(type, calculationType, 10m), actingUser));
    }

    [Fact]
    public async Task AddAdditionalChargeAsync_NegativeValue_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());
        var actingUser = ActingUser(Guid.NewGuid());
        var rateCard = await service.CreateRateCardAsync(new CreateRateCardRequest("v1", DateTimeOffset.UtcNow), actingUser);

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.AddAdditionalChargeAsync(rateCard.Id, new CreateAdditionalChargeRequest(AdditionalChargeType.Fuel, ChargeCalculationType.Percentage, -5m), actingUser));
    }

    [Fact]
    public async Task ListZoneRatesAsync_UnknownRateCard_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        Assert.Null(await service.ListZoneRatesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListAdditionalChargesAsync_UnknownRateCard_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        Assert.Null(await service.ListAdditionalChargesAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task UpsertMerchantRateCardAsync_DiscountOutOfBounds_Throws(decimal discountPercent)
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());
        var actingUser = ActingUser(Guid.NewGuid());
        var rateCard = await service.CreateRateCardAsync(new CreateRateCardRequest("v1", DateTimeOffset.UtcNow), actingUser);

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.UpsertMerchantRateCardAsync(Guid.NewGuid(), new UpsertMerchantRateCardRequest(rateCard.Id, discountPercent), actingUser));
    }

    [Fact]
    public async Task UpsertMerchantRateCardAsync_FirstCall_Inserts_SecondCall_Updates()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        var merchantId = Guid.NewGuid();

        var rateCard = await service.CreateRateCardAsync(new CreateRateCardRequest("v1", DateTimeOffset.UtcNow), actingUser);

        var inserted = await service.UpsertMerchantRateCardAsync(merchantId, new UpsertMerchantRateCardRequest(rateCard.Id, 10m), actingUser);
        Assert.Equal(10m, inserted.OverrideDiscountPercent);

        var updated = await service.UpsertMerchantRateCardAsync(merchantId, new UpsertMerchantRateCardRequest(rateCard.Id, 15m), actingUser);
        Assert.Equal(15m, updated.OverrideDiscountPercent);
        Assert.Equal(inserted.Id, updated.Id);
    }
}
