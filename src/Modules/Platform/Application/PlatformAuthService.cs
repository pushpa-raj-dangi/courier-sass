using System.Security.Cryptography;
using System.Text;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Platform.Application;

public record PlatformAuthResult(string AccessToken, string RefreshToken, Guid UserId);
public record PlatformLoginRequest(string Email, string Password);
public record PlatformRefreshRequest(string RefreshToken);

public class PlatformAuthenticationFailedException : Exception
{
    public PlatformAuthenticationFailedException(string message) : base(message) { }
}

public class PlatformAuthService
{
    private readonly DbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPlatformJwtTokenService _jwtTokenService;

    private static readonly string DummyPasswordHash =
        new Courier.Identity.Infrastructure.PasswordHasherAdapter().Hash("dummy-password-for-timing-safety");

    public PlatformAuthService(DbContext dbContext, IPasswordHasher passwordHasher, IPlatformJwtTokenService jwtTokenService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<PlatformAuthResult> LoginAsync(PlatformLoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.ToLowerInvariant();
        // OrganizationId == null is what makes a User a platform account (established by
        // AuthService.LoginAsync's own guard, Task 5) — IgnoreQueryFilters() is required here
        // exactly as it is on every other pre-authentication read in this codebase (no tenant is
        // known yet, and a SuperAdmin User row has no tenant to know).
        var user = await _dbContext.Set<User>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email && u.OrganizationId == null, cancellationToken);

        var passwordValid = _passwordHasher.Verify(request.Password, user?.PasswordHash ?? DummyPasswordHash);
        if (user is null || !passwordValid || !user.IsActive)
        {
            throw new PlatformAuthenticationFailedException("Invalid email or password.");
        }

        var refreshToken = await IssueRefreshTokenAsync(user.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user.Id, user.Email);
        return new PlatformAuthResult(accessToken, refreshToken.PlainText, user.Id);
    }

    public async Task<PlatformAuthResult> RefreshTokenAsync(PlatformRefreshRequest request, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(request.RefreshToken);
        var existing = await _dbContext.Set<RefreshToken>()
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);
        if (existing is null || existing.RevokedAt is not null || existing.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new PlatformAuthenticationFailedException("Refresh token is invalid or expired.");
        }

        var user = await _dbContext.Set<User>().IgnoreQueryFilters()
            .FirstAsync(u => u.Id == existing.UserId, cancellationToken);
        if (!user.IsActive || user.OrganizationId is not null)
        {
            throw new PlatformAuthenticationFailedException("Refresh token is invalid or expired.");
        }

        var newRefreshToken = await IssueRefreshTokenAsync(user.Id, cancellationToken);
        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenId = newRefreshToken.Entity.Id;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user.Id, user.Email);
        return new PlatformAuthResult(accessToken, newRefreshToken.PlainText, user.Id);
    }

    private async Task<(RefreshToken Entity, string PlainText)> IssueRefreshTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var plainText = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var entity = new RefreshToken { Id = Guid.NewGuid(), UserId = userId, TokenHash = HashToken(plainText), ExpiresAt = DateTimeOffset.UtcNow.AddDays(7) };
        _dbContext.Set<RefreshToken>().Add(entity);
        await Task.CompletedTask;
        return (entity, plainText);
    }

    private static string HashToken(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
