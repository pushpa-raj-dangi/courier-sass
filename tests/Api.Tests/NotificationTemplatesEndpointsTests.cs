using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Identity.Application;
using Courier.Notifications.Application;
using Courier.Notifications.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
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
}
