using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Courier.Identity.Application;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Courier.Identity.Infrastructure;

public class JwtTokenService : IJwtTokenService
{
    private readonly IOptionsMonitor<JwtOptions> _optionsMonitor;

    public JwtTokenService(IOptionsMonitor<JwtOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public string IssueAccessToken(Guid userId, Guid organizationId, string email)
    {
        var options = _optionsMonitor.CurrentValue;
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("org_id", organizationId.ToString()),
            new Claim("email", email)
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
