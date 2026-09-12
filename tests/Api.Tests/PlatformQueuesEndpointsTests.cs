using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Platform.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class PlatformQueuesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformQueuesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> LoginAsSuperAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task GetQueues_WithoutAuth_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/platform/queues");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetQueues_AsSuperAdmin_ReturnsAQueueList()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsSuperAdminAsync(client));

        var response = await client.GetAsync("/api/v1/platform/queues");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var queues = await response.Content.ReadFromJsonAsync<List<QueueSummary>>();
        Assert.NotNull(queues);
    }
}
