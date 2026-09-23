namespace Courier.Geography.Application;

// I3's consumer of I2's Country.IsServiced — the parent international-support design spec's §9
// enforcement point ("Country.IsServiced enforced server-side for sanctioned/unserviced
// destinations"). Implemented by CountryQueryService; consumed by International.Application,
// which has a project reference to Geography.Application but not to Geography.Infrastructure or
// Geography.Domain's DbContext access.
public interface ICountryLookupService
{
    Task<bool> IsServicedAsync(string isoCode, CancellationToken cancellationToken = default);
}
