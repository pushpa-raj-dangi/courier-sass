using System.Net;
using System.Net.Http.Json;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class AuthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_Login_Refresh_FullFlow_Succeeds()
    {
        var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var registerRequest = new RegisterOrganizationRequest(
            $"E2E Org {suffix}", $"e2e-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        var registerResult = await registerResponse.Content.ReadFromJsonAsync<AuthResult>();
        Assert.NotNull(registerResult);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(registerRequest.OwnerEmail, "P@ssw0rd123!"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(registerResult!.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var registerRequest = new RegisterOrganizationRequest(
            $"E2E Org {suffix}", $"e2e-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(registerRequest.OwnerEmail, "wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }
}
