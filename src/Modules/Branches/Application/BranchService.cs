using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Branches.Application;

public class BranchService : IBranchLookupService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly IGeographyLookupService _geographyLookupService;
    private readonly IBranchPlanLimitProvider _branchPlanLimitProvider;

    public BranchService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IGeographyLookupService geographyLookupService,
        IBranchPlanLimitProvider branchPlanLimitProvider)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _geographyLookupService = geographyLookupService;
        _branchPlanLimitProvider = branchPlanLimitProvider;
    }

    public async Task<BranchResponse> CreateBranchAsync(CreateBranchRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new BranchValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new BranchValidationException("Acting user could not be resolved.");

        var codeTaken = await _dbContext.Set<Branch>()
            .AnyAsync(b => b.Code == request.Code, cancellationToken);
        if (codeTaken)
        {
            throw new BranchValidationException($"Branch code '{request.Code}' is already in use.");
        }

        // _dbContext.Set<Branch>().CountAsync(...) here is already tenant-scoped by the ambient
        // query filter — CreateBranchAsync only ever runs under a real staff JWT with an org_id
        // claim (never a SuperAdmin/platform/background context), so no .IgnoreQueryFilters() is
        // needed (unlike GetMaxBranchesAsync's own implementation, which serves both contexts).
        var maxBranches = await _branchPlanLimitProvider.GetMaxBranchesAsync(organizationId, cancellationToken);
        if (maxBranches is int limit)
        {
            var currentCount = await _dbContext.Set<Branch>().CountAsync(cancellationToken);
            if (currentCount >= limit)
            {
                throw new BranchLimitExceededException($"Your plan allows a maximum of {limit} branches. Upgrade your plan to add more.");
            }
        }

        var validGeography = await _geographyLookupService.IsValidHierarchyAsync(
            request.ProvinceId, request.DistrictId, request.LocalLevelId, request.WardNumber, cancellationToken);
        if (!validGeography)
        {
            throw new BranchValidationException("Province/District/LocalLevel/WardNumber do not form a valid geography chain.");
        }

        if (request.ParentBranchId is Guid parentId)
        {
            var parentExists = await _dbContext.Set<Branch>()
                .AnyAsync(b => b.Id == parentId, cancellationToken);
            if (!parentExists)
            {
                throw new BranchValidationException("Parent branch was not found in this organization.");
            }

            var accessibleForCreate = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "branch.create", cancellationToken);
            if (accessibleForCreate is not null && !accessibleForCreate.Contains(parentId))
            {
                throw new BranchValidationException("Parent branch is outside your branch access.");
            }
        }

        if (request.DeliveryZoneId is Guid deliveryZoneId)
        {
            var zoneExists = await _geographyLookupService.DeliveryZoneExistsAsync(deliveryZoneId, cancellationToken);
            if (!zoneExists)
            {
                throw new BranchValidationException("Delivery zone was not found in this organization.");
            }
        }

        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = request.Code,
            Name = request.Name,
            Type = request.Type,
            ProvinceId = request.ProvinceId,
            DistrictId = request.DistrictId,
            LocalLevelId = request.LocalLevelId,
            WardNumber = request.WardNumber,
            AddressLine = request.AddressLine,
            Phone = request.Phone,
            OperatingHours = request.OperatingHours,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            ParentBranchId = request.ParentBranchId,
            DeliveryZoneId = request.DeliveryZoneId,
            IsActive = true
        };
        _dbContext.Set<Branch>().Add(branch);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "branch.created", "Branch", branch.Id.ToString()), cancellationToken);

        return ToResponse(branch);
    }

    public async Task<BranchResponse?> GetBranchAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var branch = await _dbContext.Set<Branch>().FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        return branch is null ? null : ToResponse(branch);
    }

    public async Task<List<BranchResponse>> ListBranchesAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, "branch.read", cancellationToken);

        var query = _dbContext.Set<Branch>().AsQueryable();
        if (accessibleBranchIds is not null)
        {
            var ids = accessibleBranchIds.ToArray();
            query = query.Where(b => ids.Contains(b.Id));
        }

        var branches = await query.ToListAsync(cancellationToken);
        return branches.Select(ToResponse).ToList();
    }

    public async Task<BranchSummary> GetSummaryAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, "branch.read", cancellationToken);

        var query = _dbContext.Set<Branch>().AsNoTracking().AsQueryable();
        if (accessibleBranchIds is not null)
        {
            var ids = accessibleBranchIds.ToArray();
            query = query.Where(b => ids.Contains(b.Id));
        }

        var branches = await query.Select(b => new { b.IsActive, b.Type }).ToListAsync(cancellationToken);
        var byType = branches.GroupBy(b => b.Type).ToDictionary(g => g.Key, g => g.Count());
        return new BranchSummary(branches.Count, branches.Count(b => b.IsActive), branches.Count(b => !b.IsActive), byType);
    }

    // No accessible-branch-id narrowing — unlike ListBranchesAsync, a merchant caller has no
    // branch-grant concept to narrow by; they need every active branch as a candidate booking
    // origin/destination. Relies on the ambient AppDbContext tenant query filter for
    // organization scoping, same as ListBranchesAsync itself does. IsActive filter is a
    // deliberate addition ListBranchesAsync doesn't have: a merchant shouldn't be offered a
    // deactivated branch to book against.
    public async Task<List<BranchResponse>> ListBranchesForMerchantAsync(CancellationToken cancellationToken = default)
    {
        var branches = await _dbContext.Set<Branch>()
            .Where(b => b.IsActive)
            .ToListAsync(cancellationToken);
        return branches.Select(ToResponse).ToList();
    }

    public async Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var branch = await _dbContext.Set<Branch>().FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        return branch is null ? null : new BranchZoneInfo(branch.Id, branch.DeliveryZoneId);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default)
    {
        if (branchIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }
        var idArray = branchIds.ToArray();
        var rows = await _dbContext.Set<Branch>()
            .Where(b => idArray.Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => r.Name);
    }

    // IgnoreQueryFilters() here is deliberate and safe — the caller (Platform's
    // OrganizationLifecycleService) always runs with ICurrentTenantProvider.OrganizationId ==
    // null, so the ambient filter would otherwise silently return 0 for every org, not the real
    // count. The explicit organizationId parameter in the Where clause is the actual, correct
    // scope.
    public async Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => await _dbContext.Set<Branch>().IgnoreQueryFilters()
            .CountAsync(b => b.OrganizationId == organizationId && b.IsActive, cancellationToken);

    public async Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Branch>().IgnoreQueryFilters().Where(b => b.OrganizationId == organizationId);
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(b => b.Code)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(b => new BranchAdminRecord(b.Id, b.Code, b.Name, b.IsActive))
            .ToListAsync(cancellationToken);
        return new PagedResult<BranchAdminRecord>(items, normalizedPage, normalizedPageSize, totalCount);
    }

    public async Task<BranchResponse?> AssignDeliveryZoneAsync(Guid branchId, AssignDeliveryZoneRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new BranchValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new BranchValidationException("Acting user could not be resolved.");

        var branch = await _dbContext.Set<Branch>().FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        if (branch is null)
        {
            return null;
        }

        var zoneExists = await _geographyLookupService.DeliveryZoneExistsAsync(request.DeliveryZoneId, cancellationToken);
        if (!zoneExists)
        {
            throw new BranchValidationException("Delivery zone was not found in this organization.");
        }

        var before = branch.DeliveryZoneId?.ToString() ?? "(none)";
        branch.DeliveryZoneId = request.DeliveryZoneId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "branch.delivery_zone_assigned", "Branch", branch.Id.ToString(), before, request.DeliveryZoneId.ToString()), cancellationToken);

        return ToResponse(branch);
    }

    private static BranchResponse ToResponse(Branch branch) => new(
        branch.Id, branch.Code, branch.Name, branch.Type,
        branch.ProvinceId, branch.DistrictId, branch.LocalLevelId, branch.WardNumber,
        branch.AddressLine, branch.Phone, branch.OperatingHours, branch.Latitude, branch.Longitude,
        branch.ParentBranchId, branch.DeliveryZoneId, branch.IsActive);
}
