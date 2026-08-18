using System.Security.Cryptography;
using Courier.Audit.Application;
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class AuthService
{
    private readonly DbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IAuditLogger _auditLogger;

    public AuthService(DbContext dbContext, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _auditLogger = auditLogger;
    }

    public async Task<AuthResult> RegisterOrganizationAsync(RegisterOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        // Pre-authentication: no tenant is known yet, so the global org-scoped
        // query filter must be bypassed for this platform-wide uniqueness check.
        var emailTaken = await _dbContext.Set<User>().IgnoreQueryFilters()
            .AnyAsync(u => u.Email == request.OwnerEmail, cancellationToken);
        if (emailTaken)
        {
            throw new AuthenticationFailedException("Email is already registered.");
        }

        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = request.OrganizationName,
            Slug = request.OrganizationSlug
        };
        _dbContext.Set<Organization>().Add(organization);

        var allPermissions = await _dbContext.Set<Permission>().ToListAsync(cancellationToken);
        var ownerRole = new Role
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Name = "OrganizationOwner",
            IsSystemDefault = true
        };
        _dbContext.Set<Role>().Add(ownerRole);
        // These Permission rows are already tracked (loaded above), so EF Core inserts
        // only the join-table rows here, not new Permission rows.
        foreach (var permission in allPermissions)
        {
            ownerRole.Permissions.Add(permission);
        }

        var owner = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Email = request.OwnerEmail,
            PasswordHash = _passwordHasher.Hash(request.OwnerPassword),
            FullName = request.OwnerFullName
        };
        _dbContext.Set<User>().Add(owner);

        _dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            RoleId = ownerRole.Id,
            OrganizationId = organization.Id,
            BranchScope = BranchScope.AllBranches,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = owner.Id
        });

        var refreshToken = await IssueRefreshTokenAsync(owner.Id, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organization.Id, owner.Id, "organization.registered", "Organization", organization.Id.ToString()), cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(owner.Id, organization.Id, owner.Email);
        return new AuthResult(accessToken, refreshToken.PlainText, owner.Id, organization.Id);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Set<User>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new AuthenticationFailedException("Invalid email or password.");
        }
        if (user.OrganizationId is null)
        {
            throw new AuthenticationFailedException("Platform accounts cannot log in through this endpoint.");
        }

        var refreshToken = await IssueRefreshTokenAsync(user.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(user.OrganizationId, user.Id, "user.login", "User", user.Id.ToString()), cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user.Id, user.OrganizationId.Value, user.Email);
        return new AuthResult(accessToken, refreshToken.PlainText, user.Id, user.OrganizationId.Value);
    }

    public async Task<AuthResult> RefreshTokenAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(request.RefreshToken);
        var existing = await _dbContext.Set<RefreshToken>()
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);

        if (existing is null || existing.RevokedAt is not null || existing.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new AuthenticationFailedException("Refresh token is invalid or expired.");
        }

        var user = await _dbContext.Set<User>().IgnoreQueryFilters()
            .FirstAsync(u => u.Id == existing.UserId, cancellationToken);

        var newRefreshToken = await IssueRefreshTokenAsync(user.Id, cancellationToken);
        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenId = newRefreshToken.Entity.Id;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user.Id, user.OrganizationId!.Value, user.Email);
        return new AuthResult(accessToken, newRefreshToken.PlainText, user.Id, user.OrganizationId.Value);
    }

    private async Task<(RefreshToken Entity, string PlainText)> IssueRefreshTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var plainText = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashToken(plainText),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        _dbContext.Set<RefreshToken>().Add(entity);
        await Task.CompletedTask;
        return (entity, plainText);
    }

    private static string HashToken(string token)
        => Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
