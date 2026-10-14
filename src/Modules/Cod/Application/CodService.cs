using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Domain;
using Courier.Events.Application;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

public partial class CodService : ICodCollectionCreationService, ICodCollectionService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly IMerchantLookupService _merchantLookupService;
    private readonly IShipmentAwbLookupService _shipmentAwbLookupService;
    private readonly IOutboxWriter _outboxWriter;

    public CodService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IBranchLookupService branchLookupService,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IMerchantLookupService merchantLookupService,
        IShipmentAwbLookupService shipmentAwbLookupService,
        IOutboxWriter outboxWriter)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchLookupService = branchLookupService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _merchantLookupService = merchantLookupService;
        _shipmentAwbLookupService = shipmentAwbLookupService;
        _outboxWriter = outboxWriter;
    }

    private async Task CheckBranchAccessAsync(ClaimsPrincipal actingUser, string permission, Guid branchId, CancellationToken cancellationToken)
    {
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, permission, cancellationToken);
        if (accessible is not null && !accessible.Contains(branchId))
        {
            throw new CodValidationException($"Branch is outside your branch access for permission '{permission}'.");
        }
    }

    // R3a: every Cod date filter is normalised to UTC (an offset like +05:45 must never reach
    // Npgsql's timestamptz as a non-UTC value) and an inverted range is a 400, never a 500.
    private static (DateTimeOffset? From, DateTimeOffset? To) NormalizeRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        var fromUtc = from?.ToUniversalTime();
        var toUtc = to?.ToUniversalTime();
        if (fromUtc is DateTimeOffset f && toUtc is DateTimeOffset t && f > t)
        {
            throw new CodValidationException("'from' must be on or before 'to'.");
        }
        return (fromUtc, toUtc);
    }

    private static CodCollectionResponse ToResponse(CODCollection collection) => new(
        collection.Id, collection.ShipmentId, collection.MerchantId, collection.BranchId, collection.ExpectedAmount,
        collection.ShipmentTotalCharge, collection.CollectedAmount, collection.Status, collection.CollectedByRiderId,
        collection.CollectedAt, collection.DeliveryProofId, collection.RiderCashSettlementId, collection.MerchantSettlementItemId,
        collection.CreatedAt);
}
