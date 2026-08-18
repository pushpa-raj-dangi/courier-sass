using Courier.Audit.Application;
using Courier.Identity.Application;
using Courier.Identity.Infrastructure;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Courier.Identity.Tests;

file class NullTenantProvider : ICurrentTenantProvider
{
    public Guid? OrganizationId => null;
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class AuthServiceTests
{
    // Applied once per test-assembly process. Api.Tests may migrate the same database
    // concurrently (Program.cs auto-migrates in Development); EF Core's migration history
    // table makes a second Migrate() a no-op, and a lost race surfaces as a duplicate-object
    // error that is safe to swallow once the schema is verified to be up to date.
    private static readonly Lazy<bool> SchemaReady = new(EnsureSchema, LazyThreadSafetyMode.ExecutionAndPublication);

    // CI supplies Postgres through ConnectionStrings__Postgres (port 5432 service container);
    // local development falls back to the docker-compose port mapping in this worktree.
    private static string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password";
    }

    private static AppDbContext CreateContextCore()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(GetConnectionString())
            .Options;
        return new AppDbContext(options, new NullTenantProvider());
    }

    private static bool EnsureSchema()
    {
        using var dbContext = CreateContextCore();
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

    private static AppDbContext CreateContext()
    {
        _ = SchemaReady.Value;
        return CreateContextCore();
    }

    private static AuthService CreateService(AppDbContext dbContext)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            SigningKey = "identity-tests-signing-key-at-least-32-bytes!",
            Issuer = "courier-tests",
            Audience = "courier-tests"
        });
        return new AuthService(dbContext, new PasswordHasherAdapter(), new JwtTokenService(jwtOptions), new NoopAuditLogger());
    }

    [Fact]
    public async Task RegisterOrganizationAsync_ThenLoginAsync_Succeeds()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Test Org {suffix}", $"test-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");

        var registerResult = await service.RegisterOrganizationAsync(request);
        Assert.NotEqual(Guid.Empty, registerResult.UserId);

        var loginResult = await service.LoginAsync(new LoginRequest(request.OwnerEmail, "P@ssw0rd123!"));
        Assert.Equal(registerResult.UserId, loginResult.UserId);
        Assert.Equal(registerResult.OrganizationId, loginResult.OrganizationId);
        Assert.False(string.IsNullOrWhiteSpace(loginResult.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(loginResult.RefreshToken));
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsAuthenticationFailed()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Test Org {suffix}", $"test-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        await service.RegisterOrganizationAsync(request);

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => service.LoginAsync(new LoginRequest(request.OwnerEmail, "wrong-password")));
    }

    [Fact]
    public async Task RefreshTokenAsync_RotatesToken()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Test Org {suffix}", $"test-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var registerResult = await service.RegisterOrganizationAsync(request);

        var refreshed = await service.RefreshTokenAsync(new RefreshRequest(registerResult.RefreshToken));

        Assert.Equal(registerResult.UserId, refreshed.UserId);
        Assert.NotEqual(registerResult.RefreshToken, refreshed.RefreshToken);

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => service.RefreshTokenAsync(new RefreshRequest(registerResult.RefreshToken)));
    }
}
