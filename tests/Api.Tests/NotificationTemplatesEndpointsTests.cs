using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Events.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Notifications.Application;
using Courier.Notifications.Domain;
using Courier.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

public class NotificationTemplatesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public NotificationTemplatesEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest($"Templates Org {suffix}", $"templates-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    // Minimal real Branch, matching CreateBranchAsync in RolesEndpointsTests.cs/
    // OrganizationSettingsEndpointsTests.cs — required to invite a staff user.
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

    // Invites, accepts, and logs in a CounterStaff user (shipment.create/read/update, pricing.read
    // — NOT notification_template.manage), returning their own AccessToken. Same
    // invite/accept-invite/login chain as RolesEndpointsTests and
    // OrganizationSettingsEndpointsTests.InviteAcceptAndLoginAsync.
    private async Task<string> InviteAcceptAndLoginAsCounterStaffAsync(HttpClient ownerClient)
    {
        var roles = await (await ownerClient.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        var role = roles!.Single(r => r.Name == "CounterStaff");
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
    public async Task GetTemplates_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/settings/notification-templates");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutTemplate_UnknownEventType_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.PutAsJsonAsync("/api/v1/settings/notification-templates", new { eventType = "not.a.real.event", channel = NotificationChannel.Email, body = "Body", isActive = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutTemplate_ValidEventType_UpsertsAndAppearsInList()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var putResponse = await client.PutAsJsonAsync("/api/v1/settings/notification-templates", new { eventType = "shipment.delivered", channel = NotificationChannel.Email, body = "Custom delivered body", isActive = true });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/v1/settings/notification-templates");
        var templates = await listResponse.Content.ReadFromJsonAsync<List<NotificationTemplateDto>>();
        Assert.Contains(templates!, t => t.EventType == "shipment.delivered" && t.Body == "Custom delivered body");
    }

    [Fact]
    public async Task PutTemplate_SameEventAndChannelTwice_UpdatesInPlaceRatherThanDuplicating()
    {
        // Proves the endpoint's update-in-place behavior end-to-end (not just at the service unit
        // level) — without this, a second PUT to the same (eventType, channel) could leave two
        // rows behind and the list would surface both instead of one updated row.
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        await client.PutAsJsonAsync("/api/v1/settings/notification-templates", new { eventType = "shipment.created", channel = NotificationChannel.Email, body = "First body", isActive = true });
        await client.PutAsJsonAsync("/api/v1/settings/notification-templates", new { eventType = "shipment.created", channel = NotificationChannel.Email, body = "Second body", isActive = true });

        var listResponse = await client.GetAsync("/api/v1/settings/notification-templates");
        var templates = await listResponse.Content.ReadFromJsonAsync<List<NotificationTemplateDto>>();
        Assert.Single(templates!, t => t.EventType == "shipment.created" && t.Channel == NotificationChannel.Email);
        Assert.Contains(templates!, t => t.Body == "Second body");
    }

    [Fact]
    public async Task Put_WithNepaliLocale_ReturnsLocaleAndListShowsBothLocales()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var enResponse = await client.PutAsJsonAsync("/api/v1/settings/notification-templates", new { eventType = "shipment.created", channel = "Email", body = "EN {Awb}", isActive = true });
        Assert.Equal(HttpStatusCode.OK, enResponse.StatusCode);
        var enBody = await enResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("en", enBody.GetProperty("locale").GetString());

        var neResponse = await client.PutAsJsonAsync("/api/v1/settings/notification-templates", new { eventType = "shipment.created", channel = "Email", body = "NE {Awb}", isActive = true, locale = "ne" });
        Assert.Equal(HttpStatusCode.OK, neResponse.StatusCode);
        var neBody = await neResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ne", neBody.GetProperty("locale").GetString());

        var listResponse = await client.GetAsync("/api/v1/settings/notification-templates");
        var templates = await listResponse.Content.ReadFromJsonAsync<List<NotificationTemplateDto>>();
        var rows = templates!.Where(t => t.EventType == "shipment.created" && t.Channel == NotificationChannel.Email).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, t => t.Locale == "en" && t.Body == "EN {Awb}");
        Assert.Contains(rows, t => t.Locale == "ne" && t.Body == "NE {Awb}");
    }

    [Fact]
    public async Task Put_WithUnsupportedLocale_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.PutAsJsonAsync("/api/v1/settings/notification-templates", new { eventType = "shipment.created", channel = "Email", body = "FR {Awb}", isActive = true, locale = "fr" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOTIFICATION_TEMPLATE_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_DoesNotReturnAnotherOrganizationsLocaleTemplates()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA);
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var putResponse = await clientA.PutAsJsonAsync("/api/v1/settings/notification-templates", new { eventType = "shipment.created", channel = "Email", body = "NE only A {Awb}", isActive = true, locale = "ne" });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var templates = await (await clientB.GetAsync("/api/v1/settings/notification-templates")).Content.ReadFromJsonAsync<List<NotificationTemplateDto>>();

        Assert.Empty(templates!);
    }

    // --- Finding 4: permission-403 and tenant-isolation coverage ---

    [Fact]
    public async Task GetTemplates_CounterStaffToken_Returns403()
    {
        // CounterStaff holds shipment.create/read/update and pricing.read but not
        // notification_template.manage, so listing templates must be refused.
        var ownerClient = _factory.CreateClient();
        var owner = await RegisterOrgAsync(ownerClient);
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var counterStaffToken = await InviteAcceptAndLoginAsCounterStaffAsync(ownerClient);

        var staffClient = _factory.CreateClient();
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", counterStaffToken);

        var response = await staffClient.GetAsync("/api/v1/settings/notification-templates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutTemplate_CounterStaffToken_Returns403()
    {
        var ownerClient = _factory.CreateClient();
        var owner = await RegisterOrgAsync(ownerClient);
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var counterStaffToken = await InviteAcceptAndLoginAsCounterStaffAsync(ownerClient);

        var staffClient = _factory.CreateClient();
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", counterStaffToken);

        var response = await staffClient.PutAsJsonAsync("/api/v1/settings/notification-templates",
            new { eventType = "shipment.created", channel = NotificationChannel.Email, body = "Should not be allowed", isActive = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListTemplates_TenantIsolation_OrgBNeverSeesOrgAsTemplate()
    {
        // Org A's owner creates a distinctive custom template; Org B's owner (registered
        // separately, no id ever passed cross-tenant) must never see it in their own list — this
        // is inherent to the tenant query filter + ambient ICurrentTenantProvider, but this test
        // locks it in per the design spec's §10 tenant-isolation requirement.
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA);
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var distinctiveBody = $"Org A only body {Guid.NewGuid():N}";
        var putResponse = await clientA.PutAsJsonAsync("/api/v1/settings/notification-templates",
            new { eventType = "shipment.created", channel = NotificationChannel.Email, body = distinctiveBody, isActive = true });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var listResponse = await clientB.GetAsync("/api/v1/settings/notification-templates");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var templates = await listResponse.Content.ReadFromJsonAsync<List<NotificationTemplateDto>>();
        Assert.DoesNotContain(templates!, t => t.Body == distinctiveBody);
    }
}
