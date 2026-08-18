using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Geography.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Geography.Application;

public class DeliveryZoneService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;

    public DeliveryZoneService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
    }

    public async Task<DeliveryZoneResponse> CreateAsync(CreateDeliveryZoneRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new DeliveryZoneValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new DeliveryZoneValidationException("Acting user could not be resolved.");

        var areas = await ValidateAndLoadAreasAsync(request.CoveredAreaIds, cancellationToken);

        var zone = new DeliveryZone
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            CoveredAreas = areas
        };
        _dbContext.Set<DeliveryZone>().Add(zone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "zone.created", "DeliveryZone", zone.Id.ToString()), cancellationToken);

        return ToResponse(zone);
    }

    public async Task<DeliveryZoneResponse?> GetAsync(Guid zoneId, CancellationToken cancellationToken = default)
    {
        var zone = await _dbContext.Set<DeliveryZone>()
            .Include(z => z.CoveredAreas)
            .FirstOrDefaultAsync(z => z.Id == zoneId, cancellationToken);
        return zone is null ? null : ToResponse(zone);
    }

    public async Task<List<DeliveryZoneResponse>> ListAsync(CancellationToken cancellationToken = default)
    {
        var zones = await _dbContext.Set<DeliveryZone>().Include(z => z.CoveredAreas).ToListAsync(cancellationToken);
        return zones.Select(ToResponse).ToList();
    }

    public async Task<DeliveryZoneResponse?> UpdateAsync(Guid zoneId, UpdateDeliveryZoneRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new DeliveryZoneValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new DeliveryZoneValidationException("Acting user could not be resolved.");

        var zone = await _dbContext.Set<DeliveryZone>()
            .Include(z => z.CoveredAreas)
            .FirstOrDefaultAsync(z => z.Id == zoneId, cancellationToken);
        if (zone is null)
        {
            return null;
        }

        var before = string.Join(",", zone.CoveredAreas.Select(a => a.Id));
        var areas = await ValidateAndLoadAreasAsync(request.CoveredAreaIds, cancellationToken);

        zone.Name = request.Name;
        zone.CoveredAreas.Clear();
        foreach (var area in areas)
        {
            zone.CoveredAreas.Add(area);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        var after = string.Join(",", zone.CoveredAreas.Select(a => a.Id));
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "zone.updated", "DeliveryZone", zone.Id.ToString(), before, after), cancellationToken);

        return ToResponse(zone);
    }

    private async Task<List<Area>> ValidateAndLoadAreasAsync(IReadOnlyList<Guid> areaIds, CancellationToken cancellationToken)
    {
        if (areaIds.Count == 0)
        {
            throw new DeliveryZoneValidationException("A delivery zone must cover at least one area.");
        }

        var distinctIds = areaIds.Distinct().ToList();
        var areas = await _dbContext.Set<Area>().Where(a => distinctIds.Contains(a.Id)).ToListAsync(cancellationToken);
        if (areas.Count != distinctIds.Count)
        {
            throw new DeliveryZoneValidationException("One or more covered area ids were not found.");
        }
        return areas;
    }

    private static DeliveryZoneResponse ToResponse(DeliveryZone zone) =>
        new(zone.Id, zone.Name, zone.CoveredAreas.Select(a => a.Id).ToList());
}
