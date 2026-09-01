using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Ndr.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public partial class NdrService
{
    public async Task<NdrResponse?> ResolveAsync(Guid ndrId, ResolveNdrRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new NdrValidationException("No active organization context.");

        var ndr = await _dbContext.Set<NDR>().FirstOrDefaultAsync(n => n.Id == ndrId, cancellationToken);
        if (ndr is null)
        {
            return null;
        }
        if (ndr.Status == NdrStatus.Resolved)
        {
            throw new NdrValidationException("This NDR is already resolved.");
        }

        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(ndr.ShipmentId, cancellationToken)
            ?? throw new NdrValidationException("The NDR's shipment was not found.");
        // Check 1 (permission) enforced at the route via .RequirePermission("ndr.resolve"); check 2
        // (branch access) here.
        await CheckBranchAccessAsync(actingUser, "ndr.resolve", scanInfo.DestinationBranchId, cancellationToken);

        switch (request.Resolution)
        {
            case NdrResolution.Reattempt:
                if (ndr.Status != NdrStatus.ReattemptScheduled)
                {
                    throw new NdrValidationException($"NDR must be ReattemptScheduled to resolve with Reattempt, was {ndr.Status}.");
                }
                await _shipmentTransitionService.ReattemptDeliveryAsync(ndr.ShipmentId, actingUser, cancellationToken);
                break;

            case NdrResolution.Rto:
            case NdrResolution.AutoRto:
                if (ndr.Status != NdrStatus.RtoRequested)
                {
                    throw new NdrValidationException($"NDR must be RtoRequested to resolve with {request.Resolution}, was {ndr.Status}.");
                }
                if (string.IsNullOrWhiteSpace(request.RtoReason))
                {
                    throw new NdrValidationException("RtoReason is required when resolving with Rto or AutoRto.");
                }
                await _shipmentTransitionService.InitiateRtoAsync(ndr.ShipmentId, actingUser, cancellationToken);
                await CreateReturnShipmentAsync(ndr.ShipmentId, organizationId, scanInfo.OriginBranchId, scanInfo.DestinationBranchId, request.RtoReason, cancellationToken);
                break;

            case NdrResolution.Other:
                // Allowed from any non-Resolved status — a catch-all for outcomes CLAUDE.md §20's
                // status/resolution lists don't otherwise name (e.g. the shipment was lost, or a
                // merchant-side cancellation makes the NDR moot). No shipment-side transition.
                break;

            default:
                throw new NdrValidationException("Unknown resolution.");
        }

        ndr.Status = NdrStatus.Resolved;
        ndr.Resolution = request.Resolution;
        ndr.ResolvedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), $"ndr.resolved_{request.Resolution.ToString().ToLowerInvariant()}", "Ndr", ndr.Id.ToString()), cancellationToken);
        return ToResponse(ndr, scanInfo.Awb);
    }

    // Note the swap: the ORIGINAL shipment's DestinationBranchId (where the failed delivery was
    // attempted) becomes the return leg's OriginBranchId, and the original's OriginBranchId becomes
    // the return leg's destination — the parcel is physically headed back the way it came.
    private async Task CreateReturnShipmentAsync(Guid originalShipmentId, Guid organizationId, Guid originalOriginBranchId, Guid originalDestinationBranchId, string rtoReason, CancellationToken cancellationToken)
    {
        // Insert-and-catch-DbUpdateException-retry on the unique (OrganizationId, ReturnAwb) index —
        // same race-handling shape as Shipment.Awb/Manifest.Code generation (Phases 6-7).
        const int maxAwbAttempts = 3;
        for (var attempt = 1; attempt <= maxAwbAttempts; attempt++)
        {
            var returnAwb = await GenerateReturnAwbAsync(organizationId, cancellationToken);
            var returnShipment = new ReturnShipment
            {
                Id = Guid.NewGuid(),
                OriginalShipmentId = originalShipmentId,
                OrganizationId = organizationId,
                ReturnAwb = returnAwb,
                RtoReason = rtoReason,
                OriginBranchId = originalDestinationBranchId,
                DestinationBranchId = originalOriginBranchId
            };
            _dbContext.Set<ReturnShipment>().Add(returnShipment);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException) when (attempt < maxAwbAttempts)
            {
                _dbContext.Entry(returnShipment).State = EntityState.Detached;
            }
        }
    }

    private async Task<string> GenerateReturnAwbAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var datePart = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var awbPrefix = $"RTO-{datePart}-";

        var existingAwbs = await _dbContext.Set<ReturnShipment>()
            .Where(r => r.OrganizationId == organizationId && r.ReturnAwb.StartsWith(awbPrefix))
            .Select(r => r.ReturnAwb)
            .ToListAsync(cancellationToken);
        var nextSequence = existingAwbs.Count == 0
            ? 1
            : existingAwbs.Select(a => int.Parse(a[^3..])).Max() + 1;

        return $"{awbPrefix}{nextSequence:D3}";
    }
}
