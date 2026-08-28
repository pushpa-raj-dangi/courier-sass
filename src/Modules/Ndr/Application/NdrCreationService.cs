using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Events.Application;
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
    private readonly IOutboxWriter _outboxWriter;

    public NdrCreationService(DbContext dbContext, IAuditLogger auditLogger, IShipmentTransitionService shipmentTransitionService, IOutboxWriter outboxWriter)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _shipmentTransitionService = shipmentTransitionService;
        _outboxWriter = outboxWriter;
    }

    public async Task CreateFromFailedAttemptAsync(Guid shipmentId, Guid organizationId, NdrReason reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        // Transition first, then persist the NDR record — same "transition, then record" ordering
        // RiderService.AssignTaskAsync (Phase 8) already established for cross-module writes.
        var ndrShipment = await _shipmentTransitionService.TransitionToNdrCreatedAsync(shipmentId, actingUser, cancellationToken);

        var ndr = new NDR
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            OrganizationId = organizationId,
            ShipmentId = shipmentId,
            Reason = reason,
            Status = NdrStatus.Open
        };
        _dbContext.Set<NDR>().Add(ndr);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "ndr.created", "Ndr", ndr.Id.ToString()), cancellationToken);

        if (ndrShipment is not null)
        {
            await _outboxWriter.WriteAsync(organizationId, "shipment.ndr_created", new { ShipmentId = shipmentId, MerchantId = ndrShipment.MerchantId, Awb = ndrShipment.Awb, NdrId = ndr.Id }, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
