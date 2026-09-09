using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Courier.Identity.Application;
using Courier.Platform.Application;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Courier.Platform.Infrastructure;

// A deliberately different shape from PlatformJwtTokenService's own token: this one carries
// "org_id" (so HttpContextCurrentTenantProvider/PermissionChecker/branch-scope checks all
// resolve exactly as they would for a real login by the target user — this codebase's JWTs
// carry no embedded permissions, everything is re-resolved from the database by sub/org_id on
// every request, so no other backend code needs to change for impersonation to work) plus
// "impersonated_by"/"is_impersonation", and NOT "is_super_admin" — an impersonation token must
// never itself pass RequireSuperAdmin.
public class ImpersonationTokenService : IImpersonationTokenService
{
    private static readonly TimeSpan ImpersonationLifetime = TimeSpan.FromMinutes(30);

    private readonly JwtOptions _options;

    public ImpersonationTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string IssueImpersonationAccessToken(Guid targetUserId, Guid organizationId, string email, Guid impersonatingSuperAdminId)
    {
        var claims = new[]
        {
            new Claim("sub", targetUserId.ToString()),
            new Claim("org_id", organizationId.ToString()),
            new Claim("email", email),
            new Claim("impersonated_by", impersonatingSuperAdminId.ToString()),
            new Claim("is_impersonation", "true")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(ImpersonationLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
