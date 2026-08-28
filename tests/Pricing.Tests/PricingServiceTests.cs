using Courier.Branches.Application;
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

file class FakeBranchLookup : IBranchLookupService
{
    private readonly Dictionary<Guid, Guid?> _zonesByBranch;
    public FakeBranchLookup(Dictionary<Guid, Guid?> zonesByBranch) => _zonesByBranch = zonesByBranch;

    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_zonesByBranch.TryGetValue(branchId, out var zoneId) ? new BranchZoneInfo(branchId, zoneId) : null);
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

public class PricingServiceTests
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

    private static (Guid OriginBranchId, Guid DestinationBranchId, Guid OriginZoneId, Guid DestinationZoneId, RateCard RateCard, ZoneRate ZoneRate) SeedBasicRoute(TestDbContext dbContext, Guid organizationId, decimal baseRate = 100m)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;

        var rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
        dbContext.Set<RateCard>().Add(rateCard);
        var zoneRate = new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = weightSlabId, BaseRate = baseRate };
        dbContext.Set<ZoneRate>().Add(zoneRate);
        dbContext.SaveChanges();

        return (originBranchId, destinationBranchId, originZoneId, destinationZoneId, rateCard, zoneRate);
    }

    private static PricingService CreateService(TestDbContext dbContext, Guid organizationId, Dictionary<Guid, Guid?> zonesByBranch) =>
        new(dbContext, new FixedTenantProvider(organizationId), new FakeBranchLookup(zonesByBranch), new AlwaysTrueMerchantLookup());

    [Fact]
    public async Task QuoteAsync_NoCharges_ReturnsBaseRateAsTotal()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        var result = await service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null));

        Assert.Equal(100m, result.BaseRate);
        Assert.Empty(result.AppliedCharges);
        Assert.Equal(100m, result.Total);
        Assert.Equal(seed.RateCard.Id, result.RateCardId);
    }

    [Fact]
    public async Task QuoteAsync_InsuranceNotRequested_IsNotApplied()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        dbContext.Set<AdditionalCharge>().Add(new AdditionalCharge { Id = Guid.NewGuid(), RateCardId = seed.RateCard.Id, Type = AdditionalChargeType.Insurance, CalculationType = ChargeCalculationType.Percentage, Value = 2m });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        var result = await service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, 1000m, false, null));

        Assert.Empty(result.AppliedCharges);
        Assert.Equal(100m, result.Total);
    }

    [Fact]
    public async Task QuoteAsync_InsuranceRequested_AppliesPercentOfDeclaredValue()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        dbContext.Set<AdditionalCharge>().Add(new AdditionalCharge { Id = Guid.NewGuid(), RateCardId = seed.RateCard.Id, Type = AdditionalChargeType.Insurance, CalculationType = ChargeCalculationType.Percentage, Value = 2m });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        var result = await service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, 1000m, true, null));

        var insuranceCharge = Assert.Single(result.AppliedCharges);
        Assert.Equal(AdditionalChargeType.Insurance, insuranceCharge.Type);
        Assert.Equal(20m, insuranceCharge.Amount); // 2% of 1000
        Assert.Equal(120m, result.Total);
    }

    [Fact]
    public async Task QuoteAsync_CodAmountZero_CodChargeNotApplied()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        dbContext.Set<AdditionalCharge>().Add(new AdditionalCharge { Id = Guid.NewGuid(), RateCardId = seed.RateCard.Id, Type = AdditionalChargeType.COD, CalculationType = ChargeCalculationType.Percentage, Value = 1m });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        var result = await service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null));

        Assert.Empty(result.AppliedCharges);
    }

    [Fact]
    public async Task QuoteAsync_FuelSurcharge_AppliesAsPercentOfBaseRate()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId, baseRate: 200m);
        dbContext.Set<AdditionalCharge>().Add(new AdditionalCharge { Id = Guid.NewGuid(), RateCardId = seed.RateCard.Id, Type = AdditionalChargeType.Fuel, CalculationType = ChargeCalculationType.Percentage, Value = 10m });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        var result = await service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null));

        var fuelCharge = Assert.Single(result.AppliedCharges);
        Assert.Equal(20m, fuelCharge.Amount); // 10% of 200 base
        Assert.Equal(220m, result.Total);
    }

    [Fact]
    public async Task QuoteAsync_MerchantWithOverride_AppliesDiscountToTotal()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        var merchantId = Guid.NewGuid();
        dbContext.Set<MerchantRateCard>().Add(new MerchantRateCard { Id = Guid.NewGuid(), MerchantId = merchantId, OrganizationId = organizationId, RateCardId = seed.RateCard.Id, OverrideDiscountPercent = 10m });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        var result = await service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, merchantId));

        Assert.Equal(10m, result.DiscountPercent);
        Assert.Equal(90m, result.Total); // 100 base - 10%
    }

    [Fact]
    public async Task QuoteAsync_WeightAtSlabUpperBound_ResolvesToNextSlab()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var lowerSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;
        var upperSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 1m && w.MaxWeightKg == 2m).Id;
        var rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
        dbContext.Set<RateCard>().Add(rateCard);
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = lowerSlabId, BaseRate = 50m });
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = upperSlabId, BaseRate = 75m });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new()
        {
            [originBranchId] = originZoneId,
            [destinationBranchId] = destinationZoneId
        });

        // Exactly 1.0kg: the lower slab (0.5-1) is exclusive at its MaxWeightKg, so 1.0kg must
        // resolve to the upper slab (1-2), proving the boundary is MinWeightKg <= w < MaxWeightKg.
        var result = await service.QuoteAsync(new PriceQuoteRequest(originBranchId, destinationBranchId, 1.0m, serviceTypeId, 0m, 0m, false, null));

        Assert.Equal(75m, result.BaseRate);
    }

    [Fact]
    public async Task QuoteAsync_OpenEndedTopSlab_ResolvesForVeryHeavyWeight()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var topSlabId = dbContext.Set<WeightSlab>().First(w => w.MaxWeightKg == null).Id;
        var rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
        dbContext.Set<RateCard>().Add(rateCard);
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = topSlabId, BaseRate = 500m });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new()
        {
            [originBranchId] = originZoneId,
            [destinationBranchId] = destinationZoneId
        });

        var result = await service.QuoteAsync(new PriceQuoteRequest(originBranchId, destinationBranchId, 250m, serviceTypeId, 0m, 0m, false, null));

        Assert.Equal(500m, result.BaseRate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task QuoteAsync_WeightNotPositive_Throws(decimal weightKg)
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, weightKg, serviceTypeId, 0m, 0m, false, null)));
    }

    [Fact]
    public async Task QuoteAsync_NegativeCodAmount_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, -1m, 0m, false, null)));
    }

    [Fact]
    public async Task QuoteAsync_NegativeDeclaredValue_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, -1m, true, null)));
    }

    [Fact]
    public async Task QuoteAsync_NoZoneRateForCombination_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var expressServiceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Express).Id;

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, expressServiceTypeId, 0m, 0m, false, null)));
    }

    [Fact]
    public async Task QuoteAsync_OriginBranchHasNoZoneAssigned_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = null,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null)));
    }
}
