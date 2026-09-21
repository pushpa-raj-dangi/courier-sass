namespace Courier.SharedKernel;

// Validates ISO-4217 *format* only (three ASCII letters, normalized to upper case). A membership
// list is deliberately out of scope: carriers may quote in currencies we have not enumerated.
// A sealed record class (not a struct) so default(CurrencyCode) cannot silently produce an
// invalid value.
public sealed record CurrencyCode
{
    public string Value { get; }

    private CurrencyCode(string value) => Value = value;

    public static CurrencyCode Npr { get; } = new("NPR");

    public static CurrencyCode Parse(string value) =>
        TryParse(value, out var code)
            ? code
            : throw new ArgumentException($"'{value}' is not a valid three-letter currency code.", nameof(value));

    public static bool TryParse(string? value, out CurrencyCode code)
    {
        code = default!;
        if (value is null || value.Length != 3)
        {
            return false;
        }

        var upper = value.ToUpperInvariant();
        foreach (var c in upper)
        {
            if (c is < 'A' or > 'Z')
            {
                return false;
            }
        }

        code = new CurrencyCode(upper);
        return true;
    }

    public override string ToString() => Value;
}
