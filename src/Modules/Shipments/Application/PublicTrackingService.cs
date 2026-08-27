using Courier.Branches.Domain;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public class PublicTrackingService
{
    private readonly DbContext _dbContext;

    public PublicTrackingService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PublicTrackingResponse?> GetByAwbAsync(string awb, CancellationToken cancellationToken = default)
    {
        // No tenant context exists on an anonymous request — deliberate IgnoreQueryFilters with an
        // explicit trusted predicate (exact Awb match, a shared-secret-strength business key), same
        // reasoning as ApiKeyAuthenticationHandler (Task 5). The AWB is the only input this endpoint
        // accepts or needs (CLAUDE.md §24); there is no tenant/org concept to leak.
        //
        // AWB uniqueness is enforced per-(OrganizationId, Awb), not globally (ShipmentConfiguration.cs) —
        // a pre-existing Phase 6 design that was safe only because every prior lookup path went through
        // the tenant query filter. This endpoint is the first one with zero tenant scope, so a same-day
        // collision between two orgs (same org-slug-derived 6-char prefix) is now a real cross-tenant
        // leak risk, not just a hypothetical. Fail closed: if more than one Shipment matches this AWB,
        // treat it as not found rather than arbitrarily picking one and leaking the wrong org's data.
        var matches = await _dbContext.Set<Shipment>().IgnoreQueryFilters()
            .Where(s => s.Awb == awb)
            .ToListAsync(cancellationToken);
        if (matches.Count != 1)
        {
            return null;
        }
        var shipment = matches[0];

        // ShipmentEvent carries no tenant filter of its own (AppDbContext.cs's documented
        // transitive-through-Shipment precedent) — no IgnoreQueryFilters needed here, there's
        // nothing to bypass.
        var events = await _dbContext.Set<ShipmentEvent>()
            .Where(e => e.ShipmentId == shipment.Id)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(cancellationToken);

        var branchIds = events.Where(e => e.BranchId is not null).Select(e => e.BranchId!.Value).Distinct().ToArray();
        var branchNames = await _dbContext.Set<Branch>().IgnoreQueryFilters()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var publicEvents = new List<PublicTrackingEvent>();
        PublicTrackingStatus? lastStatus = null;
        foreach (var e in events)
        {
            var mapped = PublicTrackingStatusMapper.Map(e.NewStatus);
            if (mapped == lastStatus)
            {
                // Collapse consecutive internal statuses that map to the same public bucket (e.g.
                // AtOriginBranch -> Sorted, both InTransit) into one entry — the public timeline
                // shows one InTransit row, not a confusing repeat.
                continue;
            }
            var location = e.BranchId is Guid branchId && branchNames.TryGetValue(branchId, out var name) ? name : null;
            publicEvents.Add(new PublicTrackingEvent(mapped, e.Timestamp.UtcDateTime, location));
            lastStatus = mapped;
        }

        return new PublicTrackingResponse(shipment.Awb, PublicTrackingStatusMapper.Map(shipment.CurrentStatus), publicEvents);
    }
}
