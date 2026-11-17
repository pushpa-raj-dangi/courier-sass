using Courier.Branches.Domain;
using Courier.Geography.Domain;
using Courier.Merchants.Domain;
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
        // the tenant query filter. This endpoint is the first one with zero tenant scope. New AWBs can
        // no longer collide across orgs (each org has a unique Organization.AwbPrefix), but AWBs issued
        // before that prefix existed can (same slug-derived prefix, same day) — a real cross-tenant
        // leak risk for those legacy rows. Fail closed: if more than one Shipment matches this AWB,
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

        // Only the merchant's public-facing DisplayName (never LegalName) and the coarse destination
        // area (municipality/district names, never ward/address/phone) are exposed. The anonymous
        // request has no tenant context, hence IgnoreQueryFilters keyed by the already-resolved
        // shipment's own ids.
        var senderName = await _dbContext.Set<Merchant>().IgnoreQueryFilters()
            .Where(m => m.Id == shipment.MerchantId)
            .Select(m => m.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
        var localLevelName = shipment.RecipientLocalLevelId is Guid localLevelId
            ? await _dbContext.Set<LocalLevel>().IgnoreQueryFilters()
                .Where(l => l.Id == localLevelId).Select(l => l.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var districtName = shipment.RecipientDistrictId is Guid districtId
            ? await _dbContext.Set<District>().IgnoreQueryFilters()
                .Where(d => d.Id == districtId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var destinationArea = PublicTrackingAreaFormatter.Format(localLevelName, districtName, shipment.RecipientCity);

        return new PublicTrackingResponse(shipment.Awb, PublicTrackingStatusMapper.Map(shipment.CurrentStatus), publicEvents,
            destinationArea, senderName);
    }
}
