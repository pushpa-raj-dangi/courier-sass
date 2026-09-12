using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Audit.Application;
using Courier.Events.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Identity.Presentation;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

public class AuditEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuditEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
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

    private static async Task<Guid> CreateBranchAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var response = await client.PostAsJsonAsync("/api/v1/branches", new
        {
            code = $"BR{suffix}",
            name = $"Branch {suffix}",
            type = "Branch",
            provinceId = GeographyIds.ProvinceId("Bagmati"),
            districtId = GeographyIds.DistrictId("Bagmati", "Kathmandu"),
            localLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City"),
            wardNumber = 1,
            addressLine = "Somewhere in Kathmandu"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task GetAuditLogs_AsOwner_ReturnsEventsRecordedSinceRegistration()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "auditowner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        await CreateBranchAsync(client);

        var response = await client.GetAsync("/api/v1/audit-logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogRecord>>();
        Assert.Contains(result!.Items, r => r.Action == "organization.registered");
        Assert.Contains(result.Items, r => r.Action == "branch.created");
    }

    [Fact]
    public async Task GetAuditLogs_OrgBCannotSeeOrgAsEvents()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "auditisoa");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        await CreateBranchAsync(clientA);

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "auditisob");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var response = await clientB.GetAsync("/api/v1/audit-logs");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogRecord>>();

        Assert.All(result!.Items, r => Assert.Equal(orgB.OrganizationId, r.OrganizationId));
    }

    [Fact]
    public async Task GetAuditLogs_SearchFilter_MatchesActionOrEntityTypeCaseInsensitively()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "auditsearch");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        await CreateBranchAsync(client);

        var response = await client.GetAsync("/api/v1/audit-logs?search=BRANCH");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogRecord>>();

        Assert.NotEmpty(result!.Items);
        Assert.All(result.Items, r => Assert.True(
            r.Action.Contains("branch", StringComparison.OrdinalIgnoreCase) ||
            r.EntityType.Contains("branch", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GetAuditLogs_Paginates_ReturnsDisjointPages()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "auditpage");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        for (var i = 0; i < 3; i++)
        {
            await CreateBranchAsync(client);
        }

        var page1 = await (await client.GetAsync("/api/v1/audit-logs?page=1&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<AuditLogRecord>>();
        var page2 = await (await client.GetAsync("/api/v1/audit-logs?page=2&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<AuditLogRecord>>();

        Assert.True(page1!.TotalCount >= 4); // organization.registered + 3 branch.created
        var page1Ids = page1.Items.Select(r => r.Id).ToHashSet();
        var page2Ids = page2!.Items.Select(r => r.Id).ToHashSet();
        Assert.Empty(page1Ids.Intersect(page2Ids));
    }

    [Fact]
    public async Task GetAuditLogs_RoleWithoutAuditReadPermission_Returns403()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "auditdenied");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var branchId = await CreateBranchAsync(client);

        var role = await (await client.PostAsJsonAsync("/api/v1/roles",
            new CreateRoleRequest("NoAuditAccess", new[] { "shipment.read" }))).Content.ReadFromJsonAsync<RoleResponse>();
        var email = $"limited-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/v1/users/invite", new InviteUserRequest(email, "Limited Person", null, role!.Id, branchId, null));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outboxMessage = await dbContext.Set<OutboxMessage>().IgnoreQueryFilters()
            .Where(m => m.EventType == "user.invited")
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync(m => m.Payload.Contains(email));
        var inviteUrl = JsonDocument.Parse(outboxMessage.Payload).RootElement.GetProperty("InviteUrl").GetString()!;
        var token = Uri.UnescapeDataString(inviteUrl.Split("token=")[1]);
        await client.PostAsJsonAsync("/api/v1/auth/accept-invite", new AcceptInviteRequest(token, "L1m1tedP@ss!"));
        var loginResult = await (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "L1m1tedP@ss!"))).Content.ReadFromJsonAsync<AuthResult>();

        var limitedClient = _factory.CreateClient();
        limitedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult!.AccessToken);
        var response = await limitedClient.GetAsync("/api/v1/audit-logs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditLogs_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/audit-logs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
