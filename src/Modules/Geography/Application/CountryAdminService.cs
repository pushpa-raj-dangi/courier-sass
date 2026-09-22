using Courier.Audit.Application;
using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Geography.Application;

public class CountryAdminService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;

    public CountryAdminService(DbContext dbContext, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
    }

    public async Task<CountryDto?> UpdateAsync(Guid countryId, UpdateCountryRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var country = await _dbContext.Set<Country>().FirstOrDefaultAsync(c => c.Id == countryId, cancellationToken);
        if (country is null)
        {
            return null;
        }

        if (request.CountryZoneId is { } zoneId && !await _dbContext.Set<CountryZone>().AnyAsync(z => z.Id == zoneId, cancellationToken))
        {
            throw new CountryValidationException("Country zone was not found.");
        }

        var before = $"isServiced={country.IsServiced},countryZoneId={country.CountryZoneId}";
        country.IsServiced = request.IsServiced;
        country.CountryZoneId = request.CountryZoneId;
        country.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        var after = $"isServiced={country.IsServiced},countryZoneId={country.CountryZoneId}";

        await _auditLogger.LogAsync(new AuditEntry(null, actingUserId, "country.updated", "Country", country.Id.ToString(), before, after, IsCrossTenantAction: true), cancellationToken);

        var zoneName = country.CountryZoneId.HasValue
            ? await _dbContext.Set<CountryZone>().Where(z => z.Id == country.CountryZoneId).Select(z => z.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        return new CountryDto(country.Id, country.IsoCode, country.NameEn, country.NameNe, country.DefaultCurrency, country.CallingCode, country.IsServiced, country.CountryZoneId, zoneName);
    }
}
