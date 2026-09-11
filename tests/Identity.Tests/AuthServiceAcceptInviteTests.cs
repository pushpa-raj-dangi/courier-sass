using System.Security.Cryptography;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.Extensions.Options;
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

file class NullBranchLookupService : IBranchLookupService
{
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(null);
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

file class NoopOrganizationSubscriptionStarter : IOrganizationSubscriptionStarter
{
    public Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class AuthServiceAcceptInviteTests
{
    private static AppDbContext CreateContext() => TestDbContextFactory.Create(new NullTenantProvider());

    private static AuthService CreateService(AppDbContext dbContext)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            SigningKey = "identity-tests-signing-key-at-least-32-bytes!",
            Issuer = "courier-tests",
            Audience = "courier-tests"
        });
        return new AuthService(dbContext, new PasswordHasherAdapter(), new JwtTokenService(jwtOptions), new NoopAuditLogger(), new NullBranchLookupService(), new NoopOrganizationSubscriptionStarter());
    }

    private static async Task<(Guid OrganizationId, Guid UserId, string PlainTextToken)> SeedInvitedUserAsync(AppDbContext dbContext, DateTimeOffset? expiresAt = null, DateTimeOffset? acceptedAt = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var organizationId = Guid.NewGuid();
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = $"Org {suffix}", Slug = $"org-{suffix}" });
        var user = new User
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Email = $"invitee-{suffix}@example.com",
            PasswordHash = new PasswordHasherAdapter().Hash(Guid.NewGuid().ToString()), FullName = "Invitee Person", IsActive = true
        };
        dbContext.Set<User>().Add(user);
        var plainTextToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plainTextToken)));
        dbContext.Set<UserInvite>().Add(new UserInvite
        {
            Id = Guid.NewGuid(), UserId = user.Id, TokenHash = tokenHash,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(7), AcceptedAt = acceptedAt
        });
        await dbContext.SaveChangesAsync();
        return (organizationId, user.Id, plainTextToken);
    }

    [Fact]
    public async Task AcceptInviteAsync_ValidToken_SetsPasswordAndReturnsAuthResult()
    {
        await using var dbContext = CreateContext();
        var (organizationId, userId, plainTextToken) = await SeedInvitedUserAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.AcceptInviteAsync(new AcceptInviteRequest(plainTextToken, "N3wP@ssword!"));

        Assert.Equal(userId, result.UserId);
        Assert.Equal(organizationId, result.OrganizationId);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));

        // The new password now actually works via the normal login path.
        var loginResult = await service.LoginAsync(new LoginRequest((await dbContext.Set<User>().FindAsync(userId))!.Email, "N3wP@ssword!"));
        Assert.Equal(userId, loginResult.UserId);
    }

    [Fact]
    public async Task AcceptInviteAsync_ExpiredInvite_Throws()
    {
        await using var dbContext = CreateContext();
        var (_, _, plainTextToken) = await SeedInvitedUserAsync(dbContext, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => service.AcceptInviteAsync(new AcceptInviteRequest(plainTextToken, "N3wP@ssword!")));
    }

    [Fact]
    public async Task AcceptInviteAsync_AlreadyAccepted_Throws()
    {
        await using var dbContext = CreateContext();
        var (_, _, plainTextToken) = await SeedInvitedUserAsync(dbContext, acceptedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => service.AcceptInviteAsync(new AcceptInviteRequest(plainTextToken, "N3wP@ssword!")));
    }

    [Fact]
    public async Task AcceptInviteAsync_WrongToken_Throws()
    {
        await using var dbContext = CreateContext();
        await SeedInvitedUserAsync(dbContext);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            service.AcceptInviteAsync(new AcceptInviteRequest(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), "N3wP@ssword!")));
    }

    [Fact]
    public async Task AcceptInviteAsync_EmptyPassword_Throws()
    {
        await using var dbContext = CreateContext();
        var (_, _, plainTextToken) = await SeedInvitedUserAsync(dbContext);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => service.AcceptInviteAsync(new AcceptInviteRequest(plainTextToken, "")));
    }
}
