using System.IdentityModel.Tokens.Jwt;
using Courier.Identity.Application;
using Courier.Identity.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Identity.Tests;

public class JwtTokenServiceTests
{
    [Fact]
    public void IssueAccessToken_IncludesExpectedClaims()
    {
        var options = new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            SigningKey = "unit-test-signing-key-at-least-32-bytes-long!",
            Issuer = "courier-tests",
            Audience = "courier-tests",
            AccessTokenMinutes = 15
        });
        var service = new JwtTokenService(options);
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        var token = service.IssueAccessToken(userId, orgId, "owner@example.com");

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(token);
        Assert.Equal(userId.ToString(), parsed.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal(orgId.ToString(), parsed.Claims.Single(c => c.Type == "org_id").Value);
        Assert.Equal("owner@example.com", parsed.Claims.Single(c => c.Type == "email").Value);
    }
}
