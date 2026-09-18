using System.IdentityModel.Tokens.Jwt;
using Courier.Merchants.Application;
using Courier.Merchants.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Merchants.Tests;

public class MerchantJwtTokenServiceTests
{
    private static MerchantJwtTokenService CreateService() => new(new StaticOptionsMonitor<MerchantJwtOptions>(new MerchantJwtOptions
    {
        SigningKey = "merchant-tests-signing-key-at-least-32-bytes!",
        Issuer = "courier-tests",
        Audience = "courier-tests"
    }));

    [Fact]
    public void IssueAccessToken_EncodesMerchantIdAndOrgIdClaims()
    {
        var service = CreateService();
        var merchantUserId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();

        var token = service.IssueAccessToken(merchantUserId, merchantId, organizationId, "owner@example.com");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(merchantUserId.ToString(), jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal(merchantId.ToString(), jwt.Claims.Single(c => c.Type == "merchant_id").Value);
        Assert.Equal(organizationId.ToString(), jwt.Claims.Single(c => c.Type == "org_id").Value);
        Assert.Equal("owner@example.com", jwt.Claims.Single(c => c.Type == "email").Value);
    }
}
