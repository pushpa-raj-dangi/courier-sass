using Courier.Audit.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Pricing.Tests;

public class FxRateServiceTests
{
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.ApplyConfiguration(new FxRateConfiguration());
    }

    private sealed class CapturingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private static (FxRateService Service, TestDbContext Db, CapturingAuditLogger Audit) Create()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"fx-{Guid.NewGuid()}")
            .Options;
        var db = new TestDbContext(options);
        var audit = new CapturingAuditLogger();
        return (new FxRateService(db, audit), db, audit);
    }

    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly CurrencyCode Usd = CurrencyCode.Parse("USD");

    [Fact]
    public async Task RecordAsync_ValidRequest_PersistsRowAndAudits()
    {
        var (service, db, audit) = Create();

        var dto = await service.RecordAsync(new RecordFxRateRequest("usd", "npr", 133.5m, null), Admin);

        Assert.Equal("USD", dto.FromCurrency);
        Assert.Equal("NPR", dto.ToCurrency);
        Assert.Equal(133.5m, dto.Rate);
        Assert.Equal("manual", dto.Source);
        Assert.Equal(Admin, dto.CreatedByUserId);
        Assert.Single(db.Set<FxRate>());
        var entry = Assert.Single(audit.Entries);
        Assert.Null(entry.OrganizationId);
        Assert.Equal(Admin, entry.UserId);
        Assert.Equal("fx_rate.recorded", entry.Action);
        Assert.Equal("FxRate", entry.EntityType);
        Assert.Equal(dto.Id.ToString(), entry.EntityId);
    }

    [Theory]
    [InlineData("US", "NPR", 1)]
    [InlineData("USD", "N1R", 1)]
    [InlineData("USD", "USD", 1)]
    [InlineData("USD", "NPR", 0)]
    [InlineData("USD", "NPR", -5)]
    public async Task RecordAsync_InvalidInput_Throws(string from, string to, double rate)
    {
        var (service, db, _) = Create();

        await Assert.ThrowsAsync<FxRateValidationException>(() =>
            service.RecordAsync(new RecordFxRateRequest(from, to, (decimal)rate, null), Admin));

        Assert.Empty(db.Set<FxRate>());
    }

    [Fact]
    public async Task RecordAsync_EffectiveAtInTheFuture_Throws()
    {
        var (service, _, _) = Create();

        await Assert.ThrowsAsync<FxRateValidationException>(() =>
            service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 133m, DateTimeOffset.UtcNow.AddHours(1)), Admin));
    }

    [Fact]
    public async Task RecordAsync_SecondRateForSamePair_AppendsAndKeepsHistory()
    {
        var (service, db, _) = Create();
        var earlier = DateTimeOffset.UtcNow.AddDays(-2);

        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 130m, earlier), Admin);
        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 135m, null), Admin);

        Assert.Equal(2, db.Set<FxRate>().Count());
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsMostRecentEffectiveRate()
    {
        var (service, _, _) = Create();
        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 130m, DateTimeOffset.UtcNow.AddDays(-2)), Admin);
        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 135m, DateTimeOffset.UtcNow.AddDays(-1)), Admin);

        var latest = await service.GetLatestAsync(Usd, CurrencyCode.Npr, null);

        Assert.Equal(135m, latest!.Rate);
    }

    [Fact]
    public async Task GetLatestAsync_AsOfInThePast_IgnoresLaterRates()
    {
        var (service, _, _) = Create();
        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 130m, DateTimeOffset.UtcNow.AddDays(-2)), Admin);
        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 135m, DateTimeOffset.UtcNow.AddDays(-1)), Admin);

        var asOf = await service.GetLatestAsync(Usd, CurrencyCode.Npr, DateTimeOffset.UtcNow.AddDays(-1.5));

        Assert.Equal(130m, asOf!.Rate);
    }

    [Fact]
    public async Task GetLatestAsync_NoRate_ReturnsNull()
    {
        var (service, _, _) = Create();

        Assert.Null(await service.GetLatestAsync(Usd, CurrencyCode.Npr, null));
    }

    [Fact]
    public async Task GetLatestAsync_DoesNotUseTheInversePair()
    {
        var (service, _, _) = Create();
        await service.RecordAsync(new RecordFxRateRequest("NPR", "USD", 0.0075m, null), Admin);

        Assert.Null(await service.GetLatestAsync(Usd, CurrencyCode.Npr, null));
    }

    [Fact]
    public async Task ConvertAsync_UsesLatestRateAndReportsRateId()
    {
        var (service, _, _) = Create();
        var recorded = await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 133.4567m, null), Admin);

        var conversion = await service.ConvertAsync(new Money(100m, Usd), CurrencyCode.Npr);

        Assert.Equal(new Money(13345.67m, CurrencyCode.Npr), conversion.Converted);
        Assert.Equal(new Money(100m, Usd), conversion.Source);
        Assert.Equal(recorded.Id, conversion.FxRateId);
        Assert.Equal(133.4567m, conversion.Rate);
    }

    [Fact]
    public async Task ConvertAsync_SameCurrency_ReturnsIdentityWithoutRateId()
    {
        var (service, _, _) = Create();

        var conversion = await service.ConvertAsync(Money.Npr(50m), CurrencyCode.Npr);

        Assert.Equal(Money.Npr(50m), conversion.Converted);
        Assert.Null(conversion.FxRateId);
        Assert.Equal(1m, conversion.Rate);
    }

    [Fact]
    public async Task ConvertAsync_NoRate_ThrowsNotFound()
    {
        var (service, _, _) = Create();

        await Assert.ThrowsAsync<FxRateNotFoundException>(() =>
            service.ConvertAsync(new Money(1m, Usd), CurrencyCode.Npr));
    }

    [Fact]
    public async Task ListAsync_FiltersByPairAndPagesNewestFirst()
    {
        var (service, _, _) = Create();
        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 130m, DateTimeOffset.UtcNow.AddDays(-3)), Admin);
        await service.RecordAsync(new RecordFxRateRequest("USD", "NPR", 135m, DateTimeOffset.UtcNow.AddDays(-1)), Admin);
        await service.RecordAsync(new RecordFxRateRequest("EUR", "NPR", 145m, DateTimeOffset.UtcNow.AddDays(-1)), Admin);

        var page = await service.ListAsync("usd", "npr", 1, 10);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new[] { 135m, 130m }, page.Items.Select(i => i.Rate).ToArray());
    }
}
