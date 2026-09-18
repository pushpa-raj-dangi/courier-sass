using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Courier.Merchants.Application;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Courier.Merchants.Infrastructure;

public class MerchantJwtTokenService : IMerchantJwtTokenService
{
    private readonly IOptionsMonitor<MerchantJwtOptions> _optionsMonitor;

    public MerchantJwtTokenService(IOptionsMonitor<MerchantJwtOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public string IssueAccessToken(Guid merchantUserId, Guid merchantId, Guid organizationId, string email)
    {
        var options = _optionsMonitor.CurrentValue;
        var claims = new[]
        {
            new Claim("sub", merchantUserId.ToString()),
            new Claim("org_id", organizationId.ToString()),
            new Claim("merchant_id", merchantId.ToString()),
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
