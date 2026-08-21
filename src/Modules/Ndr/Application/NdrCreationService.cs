using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Ndr.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public class NdrCreationService : INdrCreationService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly IShipmentTransitionService _shipmentTransitionService;

    public NdrCreationService(DbContext dbContext, IAuditLogger auditLogger, IShipmentTransitionService shipmentTransitionService)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _shipmentTransitionService = shipmentTransitionService;
    }

    public async Task CreateFromFailedAttemptAsync(Guid shipmentId, Guid organizationId, NdrReason reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        // Transition first, then persist the NDR record — same "transition, then record" ordering
        // RiderService.AssignTaskAsync (Phase 8) already established for cross-module writes.
        await _shipmentTransitionService.TransitionToNdrCreatedAsync(shipmentId, actingUser, cancellationToken);

        var ndr = new NDR
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ShipmentId = shipmentId,
            Reason = reason,
            Status = NdrStatus.Open
        };
        _dbContext.Set<NDR>().Add(ndr);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "ndr.created", "Ndr", ndr.Id.ToString()), cancellationToken);
    }
}
