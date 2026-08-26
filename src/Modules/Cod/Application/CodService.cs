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
    private readonly IOutboxWriter _outboxWriter;

    public CodService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IBranchLookupService branchLookupService,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IMerchantLookupService merchantLookupService,
        IOutboxWriter outboxWriter)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchLookupService = branchLookupService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _merchantLookupService = merchantLookupService;
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

    private static CodCollectionResponse ToResponse(CODCollection collection) => new(
        collection.Id, collection.ShipmentId, collection.MerchantId, collection.BranchId, collection.ExpectedAmount,
        collection.ShipmentTotalCharge, collection.CollectedAmount, collection.Status, collection.CollectedByRiderId,
        collection.CollectedAt, collection.DeliveryProofId, collection.RiderCashSettlementId, collection.MerchantSettlementItemId);
}
