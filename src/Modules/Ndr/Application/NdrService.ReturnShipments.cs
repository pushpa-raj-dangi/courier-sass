using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Ndr.Domain;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public partial class NdrService
{
    public async Task<ReturnShipmentResponse?> GetReturnShipmentByIdAsync(Guid returnShipmentId, CancellationToken cancellationToken = default)
    {
        var returnShipment = await _dbContext.Set<ReturnShipment>().FirstOrDefaultAsync(r => r.Id == returnShipmentId, cancellationToken);
        return returnShipment is null ? null : await ToReturnShipmentResponseAsync(returnShipment, cancellationToken);
    }

    public async Task<List<ReturnShipmentResponse>> ListReturnShipmentsAsync(CancellationToken cancellationToken = default)
    {
        var returnShipments = await _dbContext.Set<ReturnShipment>().ToListAsync(cancellationToken);
        var responses = new List<ReturnShipmentResponse>();
        foreach (var returnShipment in returnShipments)
        {
            responses.Add(await ToReturnShipmentResponseAsync(returnShipment, cancellationToken));
        }
        return responses;
    }

    public async Task<ReturnShipmentResponse?> ConfirmReturnShipmentAsync(Guid returnShipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new NdrValidationException("No active organization context.");

        var returnShipment = await _dbContext.Set<ReturnShipment>().FirstOrDefaultAsync(r => r.Id == returnShipmentId, cancellationToken);
        if (returnShipment is null)
        {
            return null;
        }
        if (returnShipment.MerchantConfirmedAt is not null)
        {
            throw new NdrValidationException("This return shipment has already been confirmed.");
        }

        // Check 1 (permission) enforced at the route via .RequirePermission("rto.manage"); check 2
        // (branch access) here, scoped to DestinationBranchId — the ORIGINAL shipment's origin
        // branch, where the parcel now physically sits after RTO_AT_ORIGIN. The underlying
        // ConfirmRtoDeliveredAsync call below enforces the state check (shipment must actually be
        // RtoAtOrigin) itself, via its own TransitionAsync guard — no need to duplicate that here.
        await CheckBranchAccessAsync(actingUser, "rto.manage", returnShipment.DestinationBranchId, cancellationToken);

        await _shipmentTransitionService.ConfirmRtoDeliveredAsync(returnShipment.OriginalShipmentId, actingUser, cancellationToken);
        returnShipment.MerchantConfirmedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "return_shipment.confirmed", "ReturnShipment", returnShipment.Id.ToString()), cancellationToken);
        return await ToReturnShipmentResponseAsync(returnShipment, cancellationToken);
    }

    // Enriches with live status/branch straight from Shipments rather than a stored, driftable
    // copy — see ReturnShipment.cs's doc comment (Task 2) for why no Status/CurrentBranchId column
    // exists on this entity at all.
    private async Task<ReturnShipmentResponse> ToReturnShipmentResponseAsync(ReturnShipment returnShipment, CancellationToken cancellationToken)
    {
        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(returnShipment.OriginalShipmentId, cancellationToken);
        return new ReturnShipmentResponse(
            returnShipment.Id, returnShipment.OriginalShipmentId, returnShipment.ReturnAwb, returnShipment.RtoReason,
            returnShipment.OriginBranchId, returnShipment.DestinationBranchId,
            scanInfo?.CurrentStatus ?? ShipmentStatus.RtoInitiated, scanInfo?.CurrentBranchId, returnShipment.MerchantConfirmedAt);
    }
}
