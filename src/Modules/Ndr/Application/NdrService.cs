using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Ndr.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public partial class NdrService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public NdrService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IShipmentTransitionService shipmentTransitionService,
        IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _shipmentTransitionService = shipmentTransitionService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    // Legal forward moves only (CLAUDE.md §20's status list, this plan's resolved design decision):
    // Open -> Contacting -> CustomerConfirmed -> {ReattemptScheduled | RtoRequested}. No backward or
    // skipped moves, and Resolved is never a legal target here — only ResolveAsync sets it, as the
    // side effect of a real resolution action, never as a plain status edit.
    private static bool IsLegalForwardMove(NdrStatus from, NdrStatus to) => (from, to) switch
    {
        (NdrStatus.Open, NdrStatus.Contacting) => true,
        (NdrStatus.Contacting, NdrStatus.CustomerConfirmed) => true,
        (NdrStatus.CustomerConfirmed, NdrStatus.ReattemptScheduled) => true,
        (NdrStatus.CustomerConfirmed, NdrStatus.RtoRequested) => true,
        _ => false
    };

    public async Task<NdrResponse?> UpdateAsync(Guid ndrId, UpdateNdrRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new NdrValidationException("No active organization context.");

        var ndr = await _dbContext.Set<NDR>().FirstOrDefaultAsync(n => n.Id == ndrId, cancellationToken);
        if (ndr is null)
        {
            return null;
        }

        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(ndr.ShipmentId, cancellationToken)
            ?? throw new NdrValidationException("The NDR's shipment was not found.");
        // Check 1 (permission) enforced at the route via .RequirePermission("ndr.manage"); check 2
        // (branch access) here, scoped to the shipment's destination branch — the branch that
        // attempted delivery and owns following up on the customer.
        await CheckBranchAccessAsync(actingUser, "ndr.manage", scanInfo.DestinationBranchId, cancellationToken);

        if (!IsLegalForwardMove(ndr.Status, request.Status))
        {
            throw new NdrValidationException($"Cannot move NDR from {ndr.Status} to {request.Status}.");
        }

        ndr.Status = request.Status;
        if (request.CustomerResponse is not null)
        {
            ndr.CustomerResponse = request.CustomerResponse;
        }
        if (request.NextAction is not null)
        {
            ndr.NextAction = request.NextAction;
        }
        if (request.FollowUpAt is not null)
        {
            ndr.FollowUpAt = request.FollowUpAt;
        }
        ndr.AssignedStaffUserId ??= actingUser.GetUserId();

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "ndr.updated", "Ndr", ndr.Id.ToString()), cancellationToken);
        return ToResponse(ndr);
    }

    private async Task CheckBranchAccessAsync(ClaimsPrincipal actingUser, string permission, Guid branchId, CancellationToken cancellationToken)
    {
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, permission, cancellationToken);
        if (accessible is not null && !accessible.Contains(branchId))
        {
            throw new NdrValidationException($"Branch is outside your branch access for permission '{permission}'.");
        }
    }

    private static NdrResponse ToResponse(NDR ndr) => new(
        ndr.Id, ndr.ShipmentId, ndr.Reason, ndr.AssignedStaffUserId, ndr.Status,
        ndr.CustomerResponse, ndr.FollowUpAt, ndr.NextAction, ndr.Resolution, ndr.ResolvedAt);
}
