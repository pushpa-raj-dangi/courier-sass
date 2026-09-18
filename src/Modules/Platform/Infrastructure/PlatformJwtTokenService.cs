using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Courier.Identity.Application;
using Courier.Platform.Application;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Courier.Platform.Infrastructure;

// Shares JwtOptions (signing key/issuer/audience) with the staff/merchant tracks — same signing
// key means all three tracks validate against the one AddJwtBearer() scheme already registered
// in Program.cs (Phase 1); "is_super_admin" is the one claim that makes a token reach any
// RequireSuperAdmin-gated route, and deliberately carries no "org_id" claim at all (a SuperAdmin
// is not a member of any organization — HttpContextCurrentTenantProvider.OrganizationId will
// correctly resolve to null for this token, exactly like a background job's ambient context).
public class PlatformJwtTokenService : IPlatformJwtTokenService
{
    private readonly IOptionsMonitor<JwtOptions> _optionsMonitor;

    public PlatformJwtTokenService(IOptionsMonitor<JwtOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public string IssueAccessToken(Guid userId, string email)
    {
        var options = _optionsMonitor.CurrentValue;
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("email", email),
            new Claim("is_super_admin", "true")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
