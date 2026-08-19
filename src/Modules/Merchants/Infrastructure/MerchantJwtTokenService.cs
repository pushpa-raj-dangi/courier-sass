using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Courier.Merchants.Application;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Courier.Merchants.Infrastructure;

public class MerchantJwtTokenService : IMerchantJwtTokenService
{
    private readonly MerchantJwtOptions _options;

    public MerchantJwtTokenService(IOptions<MerchantJwtOptions> options)
    {
        _options = options.Value;
    }

    public string IssueAccessToken(Guid merchantUserId, Guid merchantId, Guid organizationId, string email)
    {
        var claims = new[]
        {
            new Claim("sub", merchantUserId.ToString()),
            new Claim("org_id", organizationId.ToString()),
            new Claim("merchant_id", merchantId.ToString()),
            new Claim("email", email)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
