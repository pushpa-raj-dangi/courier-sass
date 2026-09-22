using Courier.Geography.Infrastructure;
using Xunit;

namespace Courier.Geography.Tests;

public class GeographyIdsCountryTests
{
    [Fact]
    public void CountryId_IsDeterministic() =>
        Assert.Equal(GeographyIds.CountryId("NP"), GeographyIds.CountryId("NP"));

    [Fact]
    public void CountryId_DiffersFromOtherIdKinds() =>
        Assert.NotEqual(GeographyIds.CountryId("NP"), GeographyIds.ProvinceId("NP"));

    [Fact]
    public void CountryId_DiffersAcrossCodes() =>
        Assert.NotEqual(GeographyIds.CountryId("NP"), GeographyIds.CountryId("IN"));
}
