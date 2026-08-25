using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Cod.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

public partial class CodService
{
    public async Task CreateExpectedAsync(Guid shipmentId, Guid organizationId, Guid merchantId, Guid branchId, decimal expectedAmount, decimal shipmentTotalCharge, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var collection = new CODCollection
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            OrganizationId = organizationId,
            MerchantId = merchantId,
            BranchId = branchId,
            ExpectedAmount = expectedAmount,
            ShipmentTotalCharge = shipmentTotalCharge,
            Status = CodCollectionStatus.CodExpected
        };
        _dbContext.Set<CODCollection>().Add(collection);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "cod.collection_expected", "CODCollection", collection.Id.ToString()), cancellationToken);
    }

    public async Task MarkCollectedAsync(Guid shipmentId, decimal collectedAmount, Guid riderId, Guid deliveryProofId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var collection = await _dbContext.Set<CODCollection>().FirstOrDefaultAsync(c => c.ShipmentId == shipmentId, cancellationToken)
            ?? throw new CodValidationException("No CODCollection exists for this shipment.");
        if (collection.Status != CodCollectionStatus.CodExpected)
        {
            throw new CodValidationException($"CODCollection must be CodExpected to record a collection, was {collection.Status}.");
        }

        collection.CollectedAmount = collectedAmount;
        collection.CollectedByRiderId = riderId;
        collection.CollectedAt = DateTimeOffset.UtcNow;
        collection.DeliveryProofId = deliveryProofId;
        collection.Status = CodCollectionStatus.CodCollected;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "cod.collected", "CODCollection", collection.Id.ToString()), cancellationToken);
    }
}
