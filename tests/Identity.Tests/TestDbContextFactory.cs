using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Courier.Identity.Tests;

internal static class TestDbContextFactory
{
    // Applied once per test-assembly process. Api.Tests may migrate the same database
    // concurrently (Program.cs auto-migrates in Development); EF Core's migration history
    // table makes a second Migrate() a no-op, and a lost race surfaces as a duplicate-object
    // error that is safe to swallow once the schema is verified to be up to date.
    private static readonly Lazy<bool> SchemaReady = new(EnsureSchema, LazyThreadSafetyMode.ExecutionAndPublication);

    // CI supplies Postgres through ConnectionStrings__Postgres (port 5432 service container);
    // local development falls back to the docker-compose port mapping in this worktree.
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
            // Another test assembly may have applied the same migration concurrently. If the
            // schema is now current, the race is benign; otherwise the failure is real.
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
