using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Geography.Domain;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public class PickupRequestService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly IBranchLookupService _branchLookupService;

    public PickupRequestService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger, IBranchAuthorizationChecker branchAuthorizationChecker, IBranchLookupService branchLookupService)
    {
        _branchLookupService = branchLookupService;
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<PickupRequestResponse> CreatePickupRequestAsync(CreatePickupRequestRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = actingUser.GetMerchantId()
            ?? throw new ShipmentValidationException("Acting merchant could not be resolved.");
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("Acting organization could not be resolved.");

        if (request.ShipmentIds is not { Length: > 0 })
        {
            throw new ShipmentValidationException("At least one shipment id is required.");
        }

        var distinctIds = request.ShipmentIds.Distinct().ToArray();
        var shipments = await _dbContext.Set<Shipment>()
            .Where(s => distinctIds.Contains(s.Id) && s.MerchantId == merchantId)
            .ToListAsync(cancellationToken);
        if (shipments.Count != distinctIds.Length)
        {
            throw new ShipmentValidationException("One or more shipment ids were not found for this merchant.");
        }

        var originBranchIds = shipments.Select(s => s.OriginBranchId).Distinct().ToList();
        if (originBranchIds.Count != 1)
        {
            throw new ShipmentValidationException("All shipments in one pickup request must share the same origin branch.");
        }

        var pickupRequest = new PickupRequest
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            MerchantId = merchantId,
            BranchId = originBranchIds[0],
            ShipmentIds = distinctIds,
            RequestedForDate = request.RequestedForDate,
            Notes = request.Notes,
            Status = PickupRequestStatus.Requested,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.Set<PickupRequest>().Add(pickupRequest);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "pickup_request.created", "PickupRequest", pickupRequest.Id.ToString()), cancellationToken);

        return (await ToResponsesAsync(new[] { pickupRequest }, cancellationToken))[0];
    }

    public async Task<List<PickupRequestResponse>> ListAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<PickupRequest>().AsQueryable();
        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            query = query.Where(p => p.MerchantId == callerMerchantId);
        }
        else
        {
            var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "pickup.read", cancellationToken);
            if (accessibleBranchIds is not null)
            {
                var accessibleBranchIdsArray = accessibleBranchIds.ToArray();
                query = query.Where(p => accessibleBranchIdsArray.Contains(p.BranchId));
            }
        }

        var results = await query.ToListAsync(cancellationToken);
        return await ToResponsesAsync(results, cancellationToken);
    }

    public async Task<PickupRequestResponse?> CancelAsync(Guid id, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = actingUser.GetMerchantId()
            ?? throw new ShipmentValidationException("Acting merchant could not be resolved.");

        var pickupRequest = await _dbContext.Set<PickupRequest>()
            .FirstOrDefaultAsync(p => p.Id == id && p.MerchantId == merchantId, cancellationToken);
        if (pickupRequest is null)
        {
            return null;
        }
        if (pickupRequest.Status is PickupRequestStatus.Completed or PickupRequestStatus.Cancelled)
        {
            throw new ShipmentValidationException($"Pickup request cannot be cancelled from status '{pickupRequest.Status}'.");
        }

        var parcelStatuses = await _dbContext.Set<Shipment>()
            .Where(s => pickupRequest.ShipmentIds.Contains(s.Id) && s.MerchantId == merchantId)
            .Select(s => s.CurrentStatus)
            .ToListAsync(cancellationToken);
        if (parcelStatuses.Any(PickupStageRules.IsPastPickup))
        {
            throw new ShipmentValidationException("A rider has already collected parcels from this pickup");
        }

        pickupRequest.Status = PickupRequestStatus.Cancelled;
        pickupRequest.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(pickupRequest.OrganizationId, actingUser.GetUserId(), "pickup_request.cancelled", "PickupRequest", pickupRequest.Id.ToString()), cancellationToken);

        return (await ToResponsesAsync(new[] { pickupRequest }, cancellationToken))[0];
    }

    private sealed record PickupParcel(
        Guid Id, string Awb, Guid MerchantId, ShipmentStatus CurrentStatus, decimal CodAmount,
        Guid? RecipientLocalLevelId, Guid? RecipientDistrictId, string? RecipientCity);

    // Batched enrichment: one query each for parcels, branch names, local levels and districts,
    // regardless of how many pickups are passed. A parcel only counts for a pickup when it belongs
    // to that pickup's merchant; ids that do not resolve are omitted.
    private async Task<List<PickupRequestResponse>> ToResponsesAsync(IReadOnlyList<PickupRequest> pickups, CancellationToken cancellationToken)
    {
        if (pickups.Count == 0)
        {
            return new List<PickupRequestResponse>();
        }

        var allIds = pickups.SelectMany(p => p.ShipmentIds).Distinct().ToArray();
        var merchantIds = pickups.Select(p => p.MerchantId).Distinct().ToArray();
        var parcels = await _dbContext.Set<Shipment>()
            .Where(s => allIds.Contains(s.Id) && merchantIds.Contains(s.MerchantId))
            .Select(s => new PickupParcel(s.Id, s.Awb, s.MerchantId, s.CurrentStatus, s.CodAmount,
                s.RecipientLocalLevelId, s.RecipientDistrictId, s.RecipientCity))
            .ToListAsync(cancellationToken);

        var branchNames = await _branchLookupService.GetNamesByIdsAsync(
            pickups.Select(p => p.BranchId).ToHashSet(), cancellationToken);

        var localLevelIds = parcels.Where(s => s.RecipientLocalLevelId is not null).Select(s => s.RecipientLocalLevelId!.Value).Distinct().ToArray();
        var districtIds = parcels.Where(s => s.RecipientDistrictId is not null).Select(s => s.RecipientDistrictId!.Value).Distinct().ToArray();
        var localLevelNames = await _dbContext.Set<LocalLevel>()
            .Where(l => localLevelIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
        var districtNames = await _dbContext.Set<District>()
            .Where(d => districtIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        var parcelsById = parcels.ToLookup(s => s.Id);
        return pickups.Select(p =>
        {
            var resolved = p.ShipmentIds
                .SelectMany(id => parcelsById[id])
                .Where(s => s.MerchantId == p.MerchantId)
                .ToList();
            var summaries = resolved.Select(s => new PickupShipmentSummary(
                s.Id, s.Awb,
                PublicTrackingAreaFormatter.Format(
                    s.RecipientLocalLevelId is Guid l && localLevelNames.TryGetValue(l, out var ln) ? ln : null,
                    s.RecipientDistrictId is Guid d && districtNames.TryGetValue(d, out var dn) ? dn : null,
                    s.RecipientCity),
                s.CodAmount == 0 ? null : s.CodAmount)).ToList();
            return new PickupRequestResponse(
                p.Id, p.MerchantId, p.BranchId, p.ShipmentIds, p.RequestedForDate, p.Notes, p.Status, p.CreatedAt, p.UpdatedAt,
                PickupStageRules.Derive(p.Status, resolved.Select(s => s.CurrentStatus).ToList()),
                branchNames.TryGetValue(p.BranchId, out var branchName) ? branchName : null,
                summaries);
        }).ToList();
    }
}
