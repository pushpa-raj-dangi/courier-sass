using Courier.Geography.Domain;
using Xunit;

namespace Courier.Geography.Tests;

public class InternationalAddressTests
{
    [Fact]
    public void Create_ValidInput_NormalizesCountryCodeToUpperCase()
    {
        var address = InternationalAddress.Create("221B Baker St", null, "London", null, "NW1 6XE", "gb");

        Assert.Equal("GB", address.CountryCode);
        Assert.Equal("221B Baker St", address.Line1);
        Assert.Null(address.Line2);
    }

    [Theory]
    [InlineData("", "London", "GB")]
    [InlineData(" ", "London", "GB")]
    public void Create_BlankLine1_Throws(string line1, string city, string countryCode) =>
        Assert.Throws<InternationalAddressValidationException>(() => InternationalAddress.Create(line1, null, city, null, null, countryCode));

    [Theory]
    [InlineData("221B Baker St", "", "GB")]
    [InlineData("221B Baker St", " ", "GB")]
    public void Create_BlankCity_Throws(string line1, string city, string countryCode) =>
        Assert.Throws<InternationalAddressValidationException>(() => InternationalAddress.Create(line1, null, city, null, null, countryCode));

    [Theory]
    [InlineData("G")]
    [InlineData("GBR")]
    [InlineData("G1")]
    [InlineData("")]
    public void Create_InvalidCountryCode_Throws(string countryCode) =>
        Assert.Throws<InternationalAddressValidationException>(() => InternationalAddress.Create("221B Baker St", null, "London", null, null, countryCode));
}
