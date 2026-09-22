namespace Courier.Geography.Domain;

public class InternationalAddressValidationException : Exception
{
    public InternationalAddressValidationException(string message) : base(message) { }
}

// Foreign postal address for a future international shipment recipient/sender (I3 — no consumer
// yet). Format validation only: no DB access, and no check that CountryCode exists in Country or
// that the country IsServiced — that policy belongs to whatever books a shipment.
public sealed record InternationalAddress
{
    public string Line1 { get; }
    public string? Line2 { get; }
    public string City { get; }
    public string? Region { get; }
    public string? PostalCode { get; }
    public string CountryCode { get; }

    private InternationalAddress(string line1, string? line2, string city, string? region, string? postalCode, string countryCode)
    {
        Line1 = line1;
        Line2 = line2;
        City = city;
        Region = region;
        PostalCode = postalCode;
        CountryCode = countryCode;
    }

    public static InternationalAddress Create(string line1, string? line2, string city, string? region, string? postalCode, string countryCode)
    {
        if (string.IsNullOrWhiteSpace(line1))
        {
            throw new InternationalAddressValidationException("Line1 is required.");
        }
        if (string.IsNullOrWhiteSpace(city))
        {
            throw new InternationalAddressValidationException("City is required.");
        }
        if (countryCode is not { Length: 2 } || !countryCode.All(char.IsAsciiLetter))
        {
            throw new InternationalAddressValidationException("CountryCode must be a two-letter country code.");
        }

        return new InternationalAddress(line1, line2, city, region, postalCode, countryCode.ToUpperInvariant());
    }
}
