using System.Security.Cryptography;
using System.Text;
using Courier.Audit.Application;
using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class MerchantAuthService
{
    private readonly DbContext _dbContext;
    private readonly IMerchantPasswordHasher _passwordHasher;
    private readonly IMerchantJwtTokenService _jwtTokenService;
    private readonly IAuditLogger _auditLogger;

    // Computed once, same timing-safety reasoning as Identity.Application.AuthService's
    // DummyPasswordHash: keeps the unknown-email and wrong-password paths costing the same
    // single hash-verify, avoiding a timing side-channel that would let an attacker enumerate
    // registered merchant-user emails.
    private static readonly string DummyPasswordHash =
        new Courier.Merchants.Infrastructure.MerchantPasswordHasherAdapter().Hash("dummy-password-for-timing-safety");

    public MerchantAuthService(DbContext dbContext, IMerchantPasswordHasher passwordHasher, IMerchantJwtTokenService jwtTokenService, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _auditLogger = auditLogger;
    }

    public async Task<MerchantAuthResult> LoginAsync(MerchantLoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.ToLowerInvariant();
        var merchantUser = await _dbContext.Set<MerchantUser>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(mu => mu.Email == email, cancellationToken);

        var passwordValid = _passwordHasher.Verify(request.Password, merchantUser?.PasswordHash ?? DummyPasswordHash);

        if (merchantUser is null || !passwordValid || !merchantUser.IsActive)
        {
            throw new MerchantAuthenticationFailedException("Invalid email or password.");
        }

        var refreshToken = await IssueRefreshTokenAsync(merchantUser.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(merchantUser.OrganizationId, merchantUser.Id, "merchant_user.login", "MerchantUser", merchantUser.Id.ToString()), cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(merchantUser.Id, merchantUser.MerchantId, merchantUser.OrganizationId, merchantUser.Email);
        return new MerchantAuthResult(accessToken, refreshToken.PlainText, merchantUser.Id, merchantUser.MerchantId, merchantUser.OrganizationId);
    }

    public async Task<MerchantAuthResult> RefreshTokenAsync(MerchantRefreshRequest request, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(request.RefreshToken);
        var existing = await _dbContext.Set<MerchantRefreshToken>()
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);

        if (existing is null || existing.RevokedAt is not null || existing.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new MerchantAuthenticationFailedException("Refresh token is invalid or expired.");
        }

        var merchantUser = await _dbContext.Set<MerchantUser>().IgnoreQueryFilters()
            .FirstAsync(mu => mu.Id == existing.MerchantUserId, cancellationToken);

        if (!merchantUser.IsActive)
        {
            throw new MerchantAuthenticationFailedException("Refresh token is invalid or expired.");
        }

        var newRefreshToken = await IssueRefreshTokenAsync(merchantUser.Id, cancellationToken);
        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenId = newRefreshToken.Entity.Id;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(merchantUser.Id, merchantUser.MerchantId, merchantUser.OrganizationId, merchantUser.Email);
        return new MerchantAuthResult(accessToken, newRefreshToken.PlainText, merchantUser.Id, merchantUser.MerchantId, merchantUser.OrganizationId);
    }

    private async Task<(MerchantRefreshToken Entity, string PlainText)> IssueRefreshTokenAsync(Guid merchantUserId, CancellationToken cancellationToken)
    {
        var plainText = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var entity = new MerchantRefreshToken
        {
            Id = Guid.NewGuid(),
            MerchantUserId = merchantUserId,
            TokenHash = HashToken(plainText),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        _dbContext.Set<MerchantRefreshToken>().Add(entity);
        await Task.CompletedTask;
        return (entity, plainText);
    }

    private static string HashToken(string token)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
