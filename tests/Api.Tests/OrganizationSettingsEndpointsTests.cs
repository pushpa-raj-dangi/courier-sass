using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Events.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Identity.Presentation;
using Courier.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

public class OrganizationSettingsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrganizationSettingsEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest($"Settings Org {suffix}", $"settings-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    // Minimal real Branch, matching CreateBranchAsync in RolesEndpointsTests.cs — required to
    // invite a staff user (InviteUserRequest requires a BranchId).
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

    // Invites, accepts, and logs in a user under roleName (using the owner's already-authorized
    // client), returning that user's own AccessToken — same invite/accept-invite/login chain as
    // RolesEndpointsTests.InviteUser_ThenAcceptInvite_ThenLoginWithNewPassword_FullLifecycle,
    // reused here to get a non-owner token with a specific, limited permission set (e.g.
    // CounterStaff: shipment.create but neither org.settings.manage nor
    // notification_template.manage).
    private async Task<string> InviteAcceptAndLoginAsync(HttpClient ownerClient, string roleName)
    {
        var roles = await (await ownerClient.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        var role = roles!.Single(r => r.Name == roleName);
        var branchId = await CreateBranchAsync(ownerClient);
        var email = $"staff-{Guid.NewGuid():N}@example.com";
        var password = "St@ffPassword1";

        var inviteResponse = await ownerClient.PostAsJsonAsync("/api/v1/users/invite",
            new InviteUserRequest(email, "Staff Person", null, role.Id, branchId, null));
        inviteResponse.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outboxMessage = await dbContext.Set<OutboxMessage>().IgnoreQueryFilters()
            .Where(m => m.EventType == "user.invited")
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync(m => m.Payload.Contains(email));
        var payload = JsonDocument.Parse(outboxMessage.Payload).RootElement;
        var inviteUrl = payload.GetProperty("InviteUrl").GetString()!;
        var token = Uri.UnescapeDataString(inviteUrl.Split("token=")[1]);

        var freshClient = _factory.CreateClient();
        var acceptResponse = await freshClient.PostAsJsonAsync("/api/v1/auth/accept-invite", new AcceptInviteRequest(token, password));
        acceptResponse.EnsureSuccessStatusCode();
        var acceptResult = await acceptResponse.Content.ReadFromJsonAsync<AuthResult>();
        return acceptResult!.AccessToken;
    }

    [Fact]
    public async Task GetOrganizationSettings_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/settings/organization");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrganizationSettings_OwnerToken_ReturnsDefaults()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/settings/organization");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OrganizationSettingsDto>();
        Assert.Equal("Asia/Kathmandu", body!.Profile.TimeZoneId);
        Assert.Equal(7, body.Security.RefreshTokenLifetimeDays);
    }

    [Fact]
    public async Task PatchProfile_OwnerToken_UpdatesAndPersists()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var profile = new OrganizationProfileDto("support@example.test", null, null, null, "Asia/Kathmandu", null);

        var patchResponse = await client.PatchAsJsonAsync("/api/v1/settings/organization/profile", profile);
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/v1/settings/organization");
        var body = await getResponse.Content.ReadFromJsonAsync<OrganizationSettingsDto>();
        Assert.Equal("support@example.test", body!.Profile.SupportEmail);
    }

    [Fact]
    public async Task PatchSecurity_InvalidRefreshDays_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.PatchAsJsonAsync("/api/v1/settings/organization/security", new SecurityPolicyDto(0, 8));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ORGANIZATION_SETTINGS_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task PatchSecurity_ValidValues_UpdatesSettings()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.PatchAsJsonAsync("/api/v1/settings/organization/security", new SecurityPolicyDto(14, 10));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var getResponse = await client.GetAsync("/api/v1/settings/organization");
        var body = await getResponse.Content.ReadFromJsonAsync<OrganizationSettingsDto>();
        Assert.Equal(14, body!.Security.RefreshTokenLifetimeDays);
    }

    [Fact]
    public async Task RevokeSessions_ExistingRefreshToken_IsRevoked()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var revokeResponse = await client.PostAsync("/api/v1/settings/organization/revoke-sessions", null);
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        // The owner's own already-issued refresh token from registration must now be revoked —
        // exercised end-to-end via the public /auth/refresh endpoint rather than reaching into the
        // database, matching how this test suite already treats RefreshToken as an implementation
        // detail elsewhere.
        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task PatchDefaults_UnknownServiceType_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.PatchAsJsonAsync("/api/v1/settings/organization/defaults", new OperationalDefaultsDto(Guid.NewGuid(), null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchDefaults_ValidServiceType_UpdatesSettings()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var serviceTypesResponse = await client.GetAsync("/api/v1/service-types");
        var serviceTypes = await serviceTypesResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
        var serviceTypeId = serviceTypes![0].GetProperty("id").GetGuid();

        var response = await client.PatchAsJsonAsync("/api/v1/settings/organization/defaults", new OperationalDefaultsDto(serviceTypeId, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var getResponse = await client.GetAsync("/api/v1/settings/organization");
        var body = await getResponse.Content.ReadFromJsonAsync<OrganizationSettingsDto>();
        Assert.Equal(serviceTypeId, body!.Defaults.DefaultServiceTypeId);
    }

    // --- Finding 2: narrow /defaults endpoint reachable by shipment-creating, non-admin roles ---

    [Fact]
    public async Task GetDefaults_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/settings/organization/defaults");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDefaults_OwnerToken_ReturnsDefaults()
    {
        // The owner holds every permission (including shipment.create), so /defaults must remain
        // reachable for them too, not just for a dedicated CounterStaff role.
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/settings/organization/defaults");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationalDefaultsDto>();
        Assert.Null(body!.DefaultServiceTypeId);
        Assert.Null(body.DefaultOriginBranchId);
    }

    [Fact]
    public async Task GetDefaults_CounterStaffToken_ReturnsDefaults()
    {
        // CounterStaff holds shipment.create but NOT org.settings.manage — this is the exact staff
        // role the shipment-creation sheet is used by, and the whole point of Finding 2's fix: the
        // full GET /api/v1/settings/organization is gated on org.settings.manage (403 for
        // CounterStaff, proven below), but the narrower /defaults route must succeed for them.
        var ownerClient = _factory.CreateClient();
        var owner = await RegisterOrgAsync(ownerClient);
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var counterStaffToken = await InviteAcceptAndLoginAsync(ownerClient, "CounterStaff");

        var staffClient = _factory.CreateClient();
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", counterStaffToken);

        var response = await staffClient.GetAsync("/api/v1/settings/organization/defaults");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationalDefaultsDto>();
        Assert.Null(body!.DefaultServiceTypeId);
    }

    // --- Finding 4: permission-403 and tenant-isolation coverage ---

    [Fact]
    public async Task GetOrganizationSettings_CounterStaffToken_Returns403()
    {
        // CounterStaff holds shipment.create/read/update and pricing.read (AdditionalRoleSeeds)
        // but not org.settings.manage, so the full settings read must be refused.
        var ownerClient = _factory.CreateClient();
        var owner = await RegisterOrgAsync(ownerClient);
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var counterStaffToken = await InviteAcceptAndLoginAsync(ownerClient, "CounterStaff");

        var staffClient = _factory.CreateClient();
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", counterStaffToken);

        var response = await staffClient.GetAsync("/api/v1/settings/organization");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOrganizationSettings_TenantIsolation_OrgBNeverSeesOrgAsProfile()
    {
        // Org A's owner customizes their profile; Org B's owner (registered separately, no id ever
        // passed cross-tenant) must still see only Org B's own (default) values — this is inherent
        // to how the tenant query filter + ambient ICurrentTenantProvider work, but this test locks
        // it in as a regression guard per the design spec's §10 tenant-isolation requirement.
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA);
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var distinctiveEmail = $"distinctive-{Guid.NewGuid():N}@example.test";
        var patchResponse = await clientA.PatchAsJsonAsync("/api/v1/settings/organization/profile",
            new OrganizationProfileDto(distinctiveEmail, null, "Org A's Secret Address", null, "Asia/Kathmandu", null));
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var getResponse = await clientB.GetAsync("/api/v1/settings/organization");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var body = await getResponse.Content.ReadFromJsonAsync<OrganizationSettingsDto>();
        Assert.Null(body!.Profile.SupportEmail);
        Assert.NotEqual(distinctiveEmail, body.Profile.SupportEmail);
        Assert.NotEqual("Org A's Secret Address", body.Profile.AddressLine);
    }
}
