using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Branches.Application;

public class BranchService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly IGeographyLookupService _geographyLookupService;

    public BranchService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IGeographyLookupService geographyLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _geographyLookupService = geographyLookupService;
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

    private static BranchResponse ToResponse(Branch branch) => new(
        branch.Id, branch.Code, branch.Name, branch.Type,
        branch.ProvinceId, branch.DistrictId, branch.LocalLevelId, branch.WardNumber,
        branch.AddressLine, branch.Phone, branch.OperatingHours, branch.Latitude, branch.Longitude,
        branch.ParentBranchId, branch.IsActive);
}
