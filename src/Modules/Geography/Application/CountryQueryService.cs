using Courier.Geography.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Geography.Application;

public class CountryQueryService : ICountryLookupService
{
    private readonly DbContext _dbContext;

    public CountryQueryService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Backs GET /api/v1/geography/countries — the address-form read, dual-auth gated. Only
    // serviced countries are offered; the full list (including unserviced rows) is admin-only.
    public async Task<List<CountryDto>> ListServicedAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Country>()
            .Where(c => c.IsServiced)
            .OrderBy(c => c.NameEn)
            .Select(c => new CountryDto(c.Id, c.IsoCode, c.NameEn, c.NameNe, c.DefaultCurrency, c.CallingCode, c.IsServiced, c.CountryZoneId, null))
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<CountryDto>> ListForAdminAsync(string? search, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var query = _dbContext.Set<Country>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLower();
            query = query.Where(c => c.IsoCode.ToLower().Contains(term) || c.NameEn.ToLower().Contains(term) || (c.NameNe != null && c.NameNe.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var countries = await query
            .OrderBy(c => c.NameEn)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var zoneIds = countries.Where(c => c.CountryZoneId.HasValue).Select(c => c.CountryZoneId!.Value).Distinct().ToList();
        var zoneNames = await _dbContext.Set<CountryZone>().Where(z => zoneIds.Contains(z.Id)).ToDictionaryAsync(z => z.Id, z => z.Name, cancellationToken);

        var items = countries
            .Select(c => new CountryDto(c.Id, c.IsoCode, c.NameEn, c.NameNe, c.DefaultCurrency, c.CallingCode, c.IsServiced, c.CountryZoneId,
                c.CountryZoneId.HasValue ? zoneNames.GetValueOrDefault(c.CountryZoneId.Value) : null))
            .ToList();
        return new PagedResult<CountryDto>(items, normalizedPage, normalizedPageSize, total);
    }

    public async Task<bool> IsServicedAsync(string isoCode, CancellationToken cancellationToken = default)
    {
        var normalized = isoCode.ToUpperInvariant();
        return await _dbContext.Set<Country>().AnyAsync(c => c.IsoCode == normalized && c.IsServiced, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string isoCode, CancellationToken cancellationToken = default)
    {
        var normalized = isoCode.ToUpperInvariant();
        return await _dbContext.Set<Country>().AnyAsync(c => c.IsoCode == normalized, cancellationToken);
    }
}
