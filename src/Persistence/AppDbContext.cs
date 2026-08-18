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

        modelBuilder.Entity<User>().HasQueryFilter(u => u.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Role>().HasQueryFilter(r => r.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<UserRole>().HasQueryFilter(ur => ur.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(a => a.OrganizationId == _currentTenantProvider.OrganizationId);
    }
}
