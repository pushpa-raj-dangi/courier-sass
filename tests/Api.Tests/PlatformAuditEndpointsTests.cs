using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Identity.Application;
using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class PlatformAuditEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformAuditEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> LoginAsSuperAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"{label} Org {suffix}", $"{label}-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task GetPlatformAuditLogs_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "pauditnsa");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/platform/audit-logs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPlatformAuditLogs_AsSuperAdmin_SeesEventsAcrossOrganizations()
    {
        var client = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(client, "pauditcrossa");
        var orgB = await RegisterOrgAsync(_factory.CreateClient(), "pauditcrossb");

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var response = await client.GetAsync("/api/v1/platform/audit-logs?search=organization.registered");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogRecordWithOrgName>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(result!.Items, r => r.OrganizationId == orgA.OrganizationId);
        Assert.Contains(result.Items, r => r.OrganizationId == orgB.OrganizationId);
    }

    [Fact]
    public async Task GetPlatformAuditLogs_FilteredByOrganizationId_ReturnsOnlyThatOrganizationsEvents()
    {
        var client = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(client, "pauditfiltera");
        await RegisterOrgAsync(_factory.CreateClient(), "pauditfilterb");

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var response = await client.GetAsync($"/api/v1/platform/audit-logs?organizationId={orgA.OrganizationId}");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogRecordWithOrgName>>();

        Assert.NotEmpty(result!.Items);
        Assert.All(result.Items, r => Assert.Equal(orgA.OrganizationId, r.OrganizationId));
    }

    [Fact]
    public async Task GetPlatformAuditLogs_FilteredByOrganizationSearch_ResolvesMatchingOrganizationNameToItsEventsOnly()
    {
        var client = _factory.CreateClient();
        var uniqueLabel = $"pauditnamesearch{Guid.NewGuid():N}"[..24];
        var matching = await RegisterOrgAsync(client, uniqueLabel);
        var nonMatching = await RegisterOrgAsync(_factory.CreateClient(), "pauditnamesearchother");

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var response = await client.GetAsync($"/api/v1/platform/audit-logs?organizationSearch={uniqueLabel}");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogRecordWithOrgName>>();

        Assert.NotEmpty(result!.Items);
        Assert.All(result.Items, r => Assert.Equal(matching.OrganizationId, r.OrganizationId));
        Assert.DoesNotContain(result.Items, r => r.OrganizationId == nonMatching.OrganizationId);
    }

    [Fact]
    public async Task GetPlatformAuditLogs_ResolvesActorNameAndOrganizationEntityName()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "pauditresolvename");

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var response = await client.GetAsync($"/api/v1/platform/audit-logs?organizationId={org.OrganizationId}&search=organization.registered");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogRecordWithOrgName>>();

        var record = Assert.Single(result!.Items);
        Assert.Equal("Owner Person", record.ActorName);
        Assert.Equal("Organization", record.EntityType);
        Assert.Equal(record.OrganizationName, record.EntityName);
    }
}
