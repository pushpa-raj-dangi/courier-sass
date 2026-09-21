using System.Globalization;

namespace Courier.SharedKernel;

public sealed class CurrencyMismatchException : InvalidOperationException
{
    public CurrencyMismatchException(CurrencyCode left, CurrencyCode right)
        : base($"Cannot combine {left} and {right} amounts without an explicit conversion.") { }
}

// Immutable amount + currency. Arithmetic is only defined within one currency; crossing
// currencies requires ConvertTo with an explicit rate (callers record the FxRate id they used —
// see FxRateService.ConvertAsync). ConvertTo rounds to two decimals: every conversion target in
// this product today is NPR.
public sealed record Money
{
    public decimal Amount { get; }
    public CurrencyCode Currency { get; }

    public Money(decimal amount, CurrencyCode currency)
    {
        Amount = amount;
        Currency = currency ?? throw new ArgumentNullException(nameof(currency));
    }

    public static Money Zero(CurrencyCode currency) => new(0m, currency);

    public static Money Npr(decimal amount) => new(amount, CurrencyCode.Npr);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public Money ConvertTo(CurrencyCode target, decimal rate)
    {
        if (rate <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "Conversion rate must be positive.");
        }
        if (target == Currency)
        {
            return this;
        }

        return new Money(Math.Round(Amount * rate, 2, MidpointRounding.AwayFromZero), target);
    }

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public override string ToString() => $"{Amount.ToString("0.00", CultureInfo.InvariantCulture)} {Currency}";

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new CurrencyMismatchException(Currency, other.Currency);
        }
    }
}
