using System.Net;
using System.Net.Http.Json;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

// Full HTTP round-trip: register an org, suspend it directly via DbContext (Task 9's real
// SuperAdmin suspend endpoint doesn't exist until later — this test only needs to prove the
// *login-side* enforcement Task 5 adds), then prove login is rejected.
//
// Uses the same WebApplicationFactory<Program> fixture TenantIsolationTests.cs already uses
// (there is no separate "ApiTestFactory" type in this project), and the real
// RegisterOrganizationRequest/AuthResult DTOs from Courier.Identity.Application (the register
// response has no `organizationId`/`ownerEmail` JSON properties — it's AuthResult:
// AccessToken/RefreshToken/UserId/OrganizationId — so the owner email is taken from the request
// this test itself sends, and OrganizationId comes off AuthResult).
public class OrganizationSuspensionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrganizationSuspensionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_OrganizationSuspended_Returns403WithOrgSuspendedCode()
    {
        var client = _factory.CreateClient();
        var suffix = $"suspend-{Guid.NewGuid():N}"[..16];
        var ownerEmail = $"owner-{suffix}@example.test";
        var registerRequest = new RegisterOrganizationRequest(
            $"Suspend Test Org {suffix}", $"org-{suffix}", ownerEmail, "P@ssw0rd123!", "Owner Person");

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var registerBody = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Courier.Persistence.AppDbContext>();
            var organization = await dbContext.Set<Courier.Identity.Domain.Organization>().IgnoreQueryFilters()
                .FirstAsync(o => o.Id == registerBody.OrganizationId);
            organization.IsActive = false;
            await dbContext.SaveChangesAsync();
        }

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(ownerEmail, "P@ssw0rd123!"));

        Assert.Equal(HttpStatusCode.Forbidden, loginResponse.StatusCode);
        var body = await loginResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("ORG_SUSPENDED", body.GetProperty("error").GetProperty("code").GetString());
    }
}
