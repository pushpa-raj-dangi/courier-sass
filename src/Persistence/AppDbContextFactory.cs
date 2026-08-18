using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Courier.Persistence;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private sealed class DesignTimeTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId => null;
    }

    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password");
        return new AppDbContext(optionsBuilder.Options, new DesignTimeTenantProvider());
    }
}
