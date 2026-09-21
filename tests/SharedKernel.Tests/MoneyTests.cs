using Courier.SharedKernel;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class MoneyTests
{
    private static readonly CurrencyCode Usd = CurrencyCode.Parse("USD");

    [Fact]
    public void Add_SameCurrency_SumsAmounts()
    {
        var result = Money.Npr(100m).Add(Money.Npr(50.25m));
        Assert.Equal(new Money(150.25m, CurrencyCode.Npr), result);
    }

    [Fact]
    public void Add_DifferentCurrency_Throws()
    {
        Assert.Throws<CurrencyMismatchException>(() => Money.Npr(1m).Add(new Money(1m, Usd)));
    }

    [Fact]
    public void Subtract_DifferentCurrency_Throws()
    {
        Assert.Throws<CurrencyMismatchException>(() => Money.Npr(1m).Subtract(new Money(1m, Usd)));
    }

    [Fact]
    public void Operators_DelegateToAddAndSubtract()
    {
        Assert.Equal(Money.Npr(3m), Money.Npr(1m) + Money.Npr(2m));
        Assert.Equal(Money.Npr(1m), Money.Npr(3m) - Money.Npr(2m));
    }

    [Fact]
    public void Multiply_ScalesAmountAndKeepsCurrency()
    {
        Assert.Equal(new Money(25m, Usd), new Money(10m, Usd).Multiply(2.5m));
    }

    [Fact]
    public void ConvertTo_AppliesRateAndRoundsToTwoDecimalsAwayFromZero()
    {
        var converted = new Money(100m, Usd).ConvertTo(CurrencyCode.Npr, 133.4567m);
        Assert.Equal(new Money(13345.67m, CurrencyCode.Npr), converted);

        var halfway = new Money(1m, Usd).ConvertTo(CurrencyCode.Npr, 0.005m);
        Assert.Equal(0.01m, halfway.Amount);
    }

    [Fact]
    public void ConvertTo_SameCurrency_ReturnsSameValue()
    {
        var value = Money.Npr(10m);
        Assert.Equal(value, value.ConvertTo(CurrencyCode.Npr, 5m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConvertTo_NonPositiveRate_Throws(double rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(1m, Usd).ConvertTo(CurrencyCode.Npr, (decimal)rate));
    }

    [Fact]
    public void Constructor_NullCurrency_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Money(1m, null!));
    }

    [Fact]
    public void Zero_HasZeroAmountInGivenCurrency()
    {
        Assert.Equal(new Money(0m, Usd), Money.Zero(Usd));
    }

    [Fact]
    public void ToString_UsesInvariantTwoDecimalFormat()
    {
        Assert.Equal("1234.50 NPR", Money.Npr(1234.5m).ToString());
    }
}
