using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Geography.Tests;

public class CountrySeedDataTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CountryZoneConfiguration());
            modelBuilder.ApplyConfiguration(new CountryConfiguration());
        }
    }

    private static TestDbContext CreateSeededContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public void SeedArray_HasNoDuplicateIsoCodes()
    {
        var codes = CountrySeedData.Countries.Select(c => c.IsoCode).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void SeedArray_EveryIsoCodeIsTwoUpperCaseLetters()
    {
        Assert.All(CountrySeedData.Countries, c =>
        {
            Assert.Equal(2, c.IsoCode.Length);
            Assert.Equal(c.IsoCode, c.IsoCode.ToUpperInvariant());
            Assert.All(c.IsoCode, ch => Assert.True(char.IsAsciiLetterUpper(ch)));
        });
    }

    [Fact]
    public void SeedArray_EveryDefaultCurrencyParsesAsCurrencyCode()
    {
        Assert.All(CountrySeedData.Countries, c =>
            Assert.True(CurrencyCode.TryParse(c.DefaultCurrency, out _), $"{c.IsoCode}: '{c.DefaultCurrency}' is not a valid currency code."));
    }

    [Fact]
    public void SeedArray_EveryCallingCodeStartsWithPlus()
    {
        Assert.All(CountrySeedData.Countries, c => Assert.StartsWith("+", c.CallingCode));
    }

    [Fact]
    public void SeedArray_NepaliNameOverrides_OnlyReferenceCodesInTheMainArray()
    {
        var codes = CountrySeedData.Countries.Select(c => c.IsoCode).ToHashSet();
        Assert.All(CountrySeedData.NameNe.Keys, key => Assert.Contains(key, codes));
    }

    [Theory]
    [InlineData("NP", "NPR", "+977")]
    [InlineData("IN", "INR", "+91")]
    [InlineData("US", "USD", "+1")]
    [InlineData("GB", "GBP", "+44")]
    [InlineData("CN", "CNY", "+86")]
    [InlineData("AU", "AUD", "+61")]
    [InlineData("AE", "AED", "+971")]
    [InlineData("QA", "QAR", "+974")]
    public void SeedArray_KnownCountry_HasExpectedCurrencyAndCallingCode(string isoCode, string currency, string callingCode)
    {
        var country = CountrySeedData.Countries.Single(c => c.IsoCode == isoCode);
        Assert.Equal(currency, country.DefaultCurrency);
        Assert.Equal(callingCode, country.CallingCode);
    }

    [Fact]
    public async Task Seed_PersistsEveryRowWithIsServicedFalseAndNoZone()
    {
        await using var dbContext = CreateSeededContext();

        var count = await dbContext.Set<Country>().CountAsync();
        Assert.Equal(CountrySeedData.Countries.Length, count);
        Assert.True(await dbContext.Set<Country>().AllAsync(c => !c.IsServiced && c.CountryZoneId == null));
    }

    [Fact]
    public async Task Seed_NepalRow_HasNoNepaliNameOverrideButKnownFields()
    {
        await using var dbContext = CreateSeededContext();

        var nepal = await dbContext.Set<Country>().SingleAsync(c => c.IsoCode == "NP");
        Assert.Equal("Nepal", nepal.NameEn);
        Assert.Null(nepal.NameNe);
        Assert.Equal("NPR", nepal.DefaultCurrency);
    }

    [Fact]
    public async Task Seed_IndiaRow_HasNepaliNameOverride()
    {
        await using var dbContext = CreateSeededContext();

        var india = await dbContext.Set<Country>().SingleAsync(c => c.IsoCode == "IN");
        Assert.Equal("भारत", india.NameNe);
    }

    [Fact]
    public async Task CountryZones_StartsEmpty()
    {
        await using var dbContext = CreateSeededContext();

        Assert.Equal(0, await dbContext.Set<CountryZone>().CountAsync());
    }
}
