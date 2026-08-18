using Courier.Audit.Domain;
using Courier.Audit.Infrastructure;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Persistence;

public class AppDbContext : DbContext
{
    private readonly ICurrentTenantProvider _currentTenantProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantProvider currentTenantProvider)
        : base(options)
    {
        _currentTenantProvider = currentTenantProvider;
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new PermissionConfiguration());
        modelBuilder.ApplyConfiguration(new UserRoleConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());

        // Tenant filters fail CLOSED: the explicit `OrganizationId != null` guard stops EF Core's
        // null-semantics translation from degrading these into `WHERE "OrganizationId" IS NULL`
        // when no tenant is resolved, which would otherwise match org-less rows (planned for
        // future platform/SuperAdmin accounts) instead of matching nothing.
        modelBuilder.Entity<User>().HasQueryFilter(u => _currentTenantProvider.OrganizationId != null && u.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Role>().HasQueryFilter(r => _currentTenantProvider.OrganizationId != null && r.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<UserRole>().HasQueryFilter(ur => _currentTenantProvider.OrganizationId != null && ur.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(a => _currentTenantProvider.OrganizationId != null && a.OrganizationId == _currentTenantProvider.OrganizationId);

        // Defensive: nothing queries Organizations with a tenant context today (registration only
        // inserts), but without this a future `Organizations.ToListAsync()` would enumerate every
        // tenant on the platform.
        modelBuilder.Entity<Organization>().HasQueryFilter(o => _currentTenantProvider.OrganizationId != null && o.Id == _currentTenantProvider.OrganizationId);

        // No tenant filter on RefreshToken: its lookups happen before the caller's tenant is known
        // (see AuthService.RefreshTokenAsync), and rows are keyed by an unguessable SHA-256 token
        // hash rather than being enumerable. Filtering it would break the refresh flow outright.
    }
}
