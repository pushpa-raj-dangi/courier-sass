using Courier.Audit.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class RoleService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;

    public RoleService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
    }

    // Role also carries a tenant HasQueryFilter in the real AppDbContext, but this explicit
    // OrganizationId clause is written anyway — this codebase's established convention (every
    // other module's list method, e.g. CodService.ListRiderSettlementsAsync) is defense-in-depth
    // filtering in code, never reliance on the ambient filter alone.
    public async Task<IReadOnlyList<RoleResponse>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RoleValidationException("No active organization context.");

        var roles = await _dbContext.Set<Role>()
            .Where(r => r.OrganizationId == organizationId)
            .Include(r => r.Permissions)
            .ToListAsync(cancellationToken);
        return roles.Select(ToResponse).ToList();
    }

    public async Task<RoleResponse> CreateRoleAsync(CreateRoleRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RoleValidationException("No active organization context.");

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new RoleValidationException("Role name is required.");
        }

        var permissions = await _dbContext.Set<Permission>()
            .Where(p => request.PermissionKeys.Contains(p.Key))
            .ToListAsync(cancellationToken);
        if (permissions.Count != request.PermissionKeys.Distinct().Count())
        {
            var unknown = request.PermissionKeys.Except(permissions.Select(p => p.Key));
            throw new RoleValidationException($"Unknown permission key(s): {string.Join(", ", unknown)}");
        }

        var role = new Role
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            IsSystemDefault = false
        };
        foreach (var permission in permissions)
        {
            role.Permissions.Add(permission);
        }
        _dbContext.Set<Role>().Add(role);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "role.created", "Role", role.Id.ToString(),
            After: string.Join(",", permissions.Select(p => p.Key))), cancellationToken);

        return ToResponse(role);
    }

    public async Task<RoleResponse> UpdateRoleAsync(Guid id, UpdateRoleRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RoleValidationException("No active organization context.");

        var role = await _dbContext.Set<Role>().Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken)
            ?? throw new RoleValidationException("Role not found.");
        if (role.IsSystemDefault)
        {
            throw new RoleValidationException("System-default roles cannot be edited.");
        }

        var permissions = await _dbContext.Set<Permission>()
            .Where(p => request.PermissionKeys.Contains(p.Key))
            .ToListAsync(cancellationToken);
        if (permissions.Count != request.PermissionKeys.Distinct().Count())
        {
            var unknown = request.PermissionKeys.Except(permissions.Select(p => p.Key));
            throw new RoleValidationException($"Unknown permission key(s): {string.Join(", ", unknown)}");
        }

        role.Permissions.Clear();
        foreach (var permission in permissions)
        {
            role.Permissions.Add(permission);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "role.updated", "Role", role.Id.ToString(),
            After: string.Join(",", permissions.Select(p => p.Key))), cancellationToken);

        return ToResponse(role);
    }

    public async Task DeleteRoleAsync(Guid id, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RoleValidationException("No active organization context.");

        var role = await _dbContext.Set<Role>()
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken)
            ?? throw new RoleValidationException("Role not found.");
        if (role.IsSystemDefault)
        {
            throw new RoleValidationException("System-default roles cannot be deleted.");
        }
        var stillAssigned = await _dbContext.Set<UserRole>()
            .AnyAsync(ur => ur.RoleId == id && ur.OrganizationId == organizationId, cancellationToken);
        if (stillAssigned)
        {
            throw new RoleValidationException("This role is still assigned to at least one user. Reassign or remove those users first.");
        }

        _dbContext.Set<Role>().Remove(role);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "role.deleted", "Role", id.ToString()), cancellationToken);
    }

    private static RoleResponse ToResponse(Role role) =>
        new(role.Id, role.Name, role.IsSystemDefault, role.Permissions.Select(p => p.Key).ToList());
}
