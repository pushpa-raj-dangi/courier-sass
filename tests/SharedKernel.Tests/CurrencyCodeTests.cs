using Courier.SharedKernel;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class CurrencyCodeTests
{
    [Theory]
    [InlineData("USD", "USD")]
    [InlineData("usd", "USD")]
    [InlineData("nPr", "NPR")]
    public void Parse_ValidCode_NormalizesToUpperCase(string input, string expected)
    {
        Assert.Equal(expected, CurrencyCode.Parse(input).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U5D")]
    [InlineData("US ")]
    public void TryParse_InvalidCode_ReturnsFalse(string? input)
    {
        Assert.False(CurrencyCode.TryParse(input, out _));
    }

    [Fact]
    public void Parse_InvalidCode_Throws()
    {
        Assert.Throws<ArgumentException>(() => CurrencyCode.Parse("12"));
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(CurrencyCode.Parse("usd"), CurrencyCode.Parse("USD"));
        Assert.NotEqual(CurrencyCode.Npr, CurrencyCode.Parse("USD"));
    }

    [Fact]
    public void ToString_ReturnsCode() => Assert.Equal("NPR", CurrencyCode.Npr.ToString());
}
