using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Manifests.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Manifests.Application;

public partial class ManifestService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IShipmentReportingLookupService _shipmentReportingLookupService;

    public ManifestService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IShipmentTransitionService shipmentTransitionService,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IBranchLookupService branchLookupService,
        IShipmentReportingLookupService shipmentReportingLookupService)
    {
        _shipmentReportingLookupService = shipmentReportingLookupService;
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _shipmentTransitionService = shipmentTransitionService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _branchLookupService = branchLookupService;
    }

    public async Task<ManifestResponse> CreateManifestAsync(CreateManifestRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ManifestValidationException("No active organization context.");

        if (request.FromBranchId == request.ToBranchId)
        {
            throw new ManifestValidationException("FromBranchId and ToBranchId must be different branches.");
        }
        if (await _branchLookupService.GetZoneInfoAsync(request.FromBranchId, cancellationToken) is null)
        {
            throw new ManifestValidationException("FromBranchId does not refer to a branch in this organization.");
        }
        if (await _branchLookupService.GetZoneInfoAsync(request.ToBranchId, cancellationToken) is null)
        {
            throw new ManifestValidationException("ToBranchId does not refer to a branch in this organization.");
        }

        await CheckBranchAccessAsync(actingUser, "manifest.create", request.FromBranchId, cancellationToken);

        var actingUserId = actingUser.GetUserId()
            ?? throw new ManifestValidationException("Acting user could not be resolved.");

        // Insert-and-catch-DbUpdateException-retry on the unique (OrganizationId, Code) index —
        // same race-handling shape as Shipment.Awb generation (Phase 6, commit a83c320's pattern).
        const int maxCodeAttempts = 3;
        Manifest manifest = null!;
        for (var attempt = 1; attempt <= maxCodeAttempts; attempt++)
        {
            var code = await GenerateManifestCodeAsync(organizationId, cancellationToken);
            manifest = new Manifest
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Code = code,
                Purpose = request.Purpose,
                FromBranchId = request.FromBranchId,
                ToBranchId = request.ToBranchId,
                Status = ManifestStatus.Draft,
                ShipmentCount = 0,
                CreatedByUserId = actingUserId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _dbContext.Set<Manifest>().Add(manifest);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException) when (attempt < maxCodeAttempts)
            {
                _dbContext.Entry(manifest).State = EntityState.Detached;
            }
        }

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "manifest.created", "Manifest", manifest.Id.ToString()), cancellationToken);
        return await LoadResponseAsync(manifest.Id, cancellationToken);
    }

    // Format: MNF-{yyyyMMdd}-{3-digit sequence}, e.g. MNF-20260818-001 (BRANCH-OPERATIONS.md §2's
    // literal example) — unique per (OrganizationId, Code). Sequence: current max existing suffix
    // for (OrganizationId, today's date-prefix) + 1. Does NOT itself guarantee uniqueness under
    // concurrency; CreateManifestAsync's catch-DbUpdateException-retry loop is what makes this
    // safe, exactly like Shipment.Awb generation (Phase 6).
    private async Task<string> GenerateManifestCodeAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var datePart = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var codePrefix = $"MNF-{datePart}-";

        var existingCodes = await _dbContext.Set<Manifest>()
            .Where(m => m.OrganizationId == organizationId && m.Code.StartsWith(codePrefix))
            .Select(m => m.Code)
            .ToListAsync(cancellationToken);
        var nextSequence = existingCodes.Count == 0
            ? 1
            : existingCodes.Select(c => int.Parse(c[^3..])).Max() + 1;

        return $"{codePrefix}{nextSequence:D3}";
    }

    private async Task CheckBranchAccessAsync(ClaimsPrincipal actingUser, string permission, Guid branchId, CancellationToken cancellationToken)
    {
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, permission, cancellationToken);
        if (accessible is not null && !accessible.Contains(branchId))
        {
            throw new ManifestValidationException($"Branch is outside your branch access for permission '{permission}'.");
        }
    }

    private async Task<ManifestResponse> LoadResponseAsync(Guid manifestId, CancellationToken cancellationToken)
    {
        var manifest = await _dbContext.Set<Manifest>().FirstAsync(m => m.Id == manifestId, cancellationToken);
        return ToResponse(manifest);
    }

    private static ManifestResponse ToResponse(Manifest manifest) => new(
        manifest.Id, manifest.Code, manifest.FromBranchId, manifest.ToBranchId, manifest.Purpose,
        manifest.VehiclePlate, manifest.DriverId, manifest.Status, manifest.ShipmentCount, manifest.CreatedAt);
}
