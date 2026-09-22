using Courier.Audit.Application;
using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Geography.Application;

public class CountryZoneService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;

    public CountryZoneService(DbContext dbContext, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
    }

    public async Task<List<CountryZoneDto>> ListAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Set<CountryZone>().OrderBy(z => z.Name).Select(z => new CountryZoneDto(z.Id, z.Name, z.Description)).ToListAsync(cancellationToken);

    public async Task<CountryZoneDto> CreateAsync(CreateCountryZoneRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CountryZoneValidationException("Name is required.");
        }
        if (await _dbContext.Set<CountryZone>().AnyAsync(z => z.Name == name, cancellationToken))
        {
            throw new CountryZoneValidationException("A country zone with this name already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var zone = new CountryZone { Id = Guid.NewGuid(), Name = name, Description = request.Description, CreatedAt = now, UpdatedAt = now };
        _dbContext.Set<CountryZone>().Add(zone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(null, actingUserId, "country_zone.created", "CountryZone", zone.Id.ToString(), After: name, IsCrossTenantAction: true), cancellationToken);
        return new CountryZoneDto(zone.Id, zone.Name, zone.Description);
    }

    public async Task<CountryZoneDto?> UpdateAsync(Guid zoneId, UpdateCountryZoneRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var zone = await _dbContext.Set<CountryZone>().FirstOrDefaultAsync(z => z.Id == zoneId, cancellationToken);
        if (zone is null)
        {
            return null;
        }
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CountryZoneValidationException("Name is required.");
        }
        if (await _dbContext.Set<CountryZone>().AnyAsync(z => z.Id != zoneId && z.Name == name, cancellationToken))
        {
            throw new CountryZoneValidationException("A country zone with this name already exists.");
        }

        var before = zone.Name;
        zone.Name = name;
        zone.Description = request.Description;
        zone.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(null, actingUserId, "country_zone.updated", "CountryZone", zone.Id.ToString(), before, name, IsCrossTenantAction: true), cancellationToken);
        return new CountryZoneDto(zone.Id, zone.Name, zone.Description);
    }
}
