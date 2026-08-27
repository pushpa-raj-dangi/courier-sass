using System.Security.Claims;
using Courier.Audit.Application;
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

    public PickupRequestService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger, IBranchAuthorizationChecker branchAuthorizationChecker)
    {
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

        return ToResponse(pickupRequest);
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
        return results.Select(ToResponse).ToList();
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

        pickupRequest.Status = PickupRequestStatus.Cancelled;
        pickupRequest.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(pickupRequest.OrganizationId, actingUser.GetUserId(), "pickup_request.cancelled", "PickupRequest", pickupRequest.Id.ToString()), cancellationToken);

        return ToResponse(pickupRequest);
    }

    private static PickupRequestResponse ToResponse(PickupRequest p) => new(
        p.Id, p.MerchantId, p.BranchId, p.ShipmentIds, p.RequestedForDate, p.Notes, p.Status, p.CreatedAt, p.UpdatedAt);
}
