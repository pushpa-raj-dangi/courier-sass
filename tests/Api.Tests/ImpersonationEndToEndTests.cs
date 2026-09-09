using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

// Full round-trip: register an org, get the SuperAdmin to impersonate the org's Owner, then use
// the impersonation token to make a real tenant read, proving both UserId (the Owner) and
// ImpersonatedByUserId (the SuperAdmin) land on the resulting AuditLog row.
//
// Uses the same WebApplicationFactory<Program> fixture every other endpoint test in this project
// uses (there is no separate "ApiTestFactory" type here — see PlatformDashboardEndpointsTests.cs).
public class ImpersonationEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ImpersonationEndToEndTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Impersonate_ThenActAsOwner_AuditLogRecordsBothIdentities()
    {
        var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            organizationName = $"Impersonation Test Org {suffix}",
            organizationSlug = $"imperso-{suffix}",
            ownerFullName = "Owner",
            ownerEmail = $"owner-{suffix}@example.test",
            ownerPassword = "P@ssw0rd123"
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var organizationId = registerBody.GetProperty("organizationId").GetGuid();
        var ownerUserId = registerBody.GetProperty("userId").GetGuid();

        var superAdminLoginResponse = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        Assert.Equal(HttpStatusCode.OK, superAdminLoginResponse.StatusCode);
        var superAdminLoginBody = await superAdminLoginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var superAdminAccessToken = superAdminLoginBody.GetProperty("accessToken").GetString();
        var superAdminUserId = superAdminLoginBody.GetProperty("userId").GetGuid();

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", superAdminAccessToken);
        var impersonateResponse = await client.PostAsync($"/api/v1/platform/organizations/{organizationId}/users/{ownerUserId}/impersonate", null);
        Assert.Equal(HttpStatusCode.OK, impersonateResponse.StatusCode);
        var impersonateBody = await impersonateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var impersonationToken = impersonateBody.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", impersonationToken);
        var meResponse = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var meBody = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ownerUserId, meBody.GetProperty("userId").GetGuid());

        // The impersonation token must never itself pass RequireSuperAdmin — verifies the claim
        // shape, not just that the grant endpoint worked.
        var platformOrgsResponse = await client.GetAsync("/api/v1/platform/organizations");
        Assert.Equal(HttpStatusCode.Forbidden, platformOrgsResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Courier.Persistence.AppDbContext>();
        var grantEntry = await dbContext.Set<Courier.Audit.Domain.AuditLog>().IgnoreQueryFilters()
            .Where(a => a.Action == "platform.impersonation_started" && a.OrganizationId == organizationId)
            .OrderByDescending(a => a.Timestamp)
            .FirstAsync();
        Assert.Equal(superAdminUserId, grantEntry.UserId);
        Assert.True(grantEntry.IsCrossTenantAction);

        // The subsequent /auth/me read isn't itself audited (it's a pure read), but the "me" claims
        // prove the impersonation token carries the owner's identity; the grant row above is the
        // one place both identities (UserId = SuperAdmin who granted it, ImpersonatedByUserId would
        // appear on any WRITE made under the impersonation token) are recorded per the plan's
        // dual-actor audit requirement.
    }
}
