using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Courier.Ndr.Tests;

internal static class TestDbContextFactory
{
    private static readonly Lazy<bool> SchemaReady = new(EnsureSchema, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password";
    }

    public static AppDbContext Create(ICurrentTenantProvider tenantProvider)
    {
        _ = SchemaReady.Value;
        return CreateCore(tenantProvider);
    }

    private static AppDbContext CreateCore(ICurrentTenantProvider tenantProvider)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(GetConnectionString())
            .Options;
        return new AppDbContext(options, tenantProvider);
    }

    private static bool EnsureSchema()
    {
        using var dbContext = CreateCore(new NullTenantProvider());
        try
        {
            dbContext.Database.Migrate();
        }
        catch (PostgresException)
        {
            if (dbContext.Database.GetPendingMigrations().Any())
            {
                throw;
            }
        }
        return true;
    }

    private sealed class NullTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId => null;
    }
}
