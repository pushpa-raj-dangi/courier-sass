namespace Courier.Geography.Application;

public sealed record CountryDto(Guid Id, string IsoCode, string NameEn, string? NameNe, string DefaultCurrency, string CallingCode, bool IsServiced, Guid? CountryZoneId, string? CountryZoneName);

public sealed record UpdateCountryRequest(bool IsServiced, Guid? CountryZoneId);

public sealed record CountryZoneDto(Guid Id, string Name, string? Description);

public sealed record CreateCountryZoneRequest(string Name, string? Description);

public sealed record UpdateCountryZoneRequest(string Name, string? Description);

public class CountryValidationException : Exception
{
    public CountryValidationException(string message) : base(message) { }
}

public class CountryZoneValidationException : Exception
{
    public CountryZoneValidationException(string message) : base(message) { }
}
