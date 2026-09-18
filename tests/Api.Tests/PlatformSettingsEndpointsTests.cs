using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class PlatformSettingsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformSettingsEndpointsTests(WebApplicationFactory<Program> factory)
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
    public async Task GetSettings_AsSuperAdmin_ReturnsCatalog()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/platform/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.EnumerateArray().Any(s => s.GetProperty("key").GetString() == "Frontend.BaseUrl"));
    }

    [Fact]
    public async Task UpdateSetting_NonSecretValue_ReturnsNoContentAndPersists()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var marker = $"https://updated-{Guid.NewGuid():N}.test";

        try
        {
            var putResponse = await client.PutAsJsonAsync("/api/v1/platform/settings/Frontend.BaseUrl", new { value = marker, confirmationPhrase = (string?)null });
            Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

            var getResponse = await client.GetAsync("/api/v1/platform/settings");
            var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
            var setting = body.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "Frontend.BaseUrl");
            Assert.Equal(marker, setting.GetProperty("displayValue").GetString());
        }
        finally
        {
            // This test persists an override on an EXISTING seeded catalog key against the shared
            // dev Postgres (see PlatformSettingsServiceTests.cs for the same discipline). Reset it
            // back so other tests/tasks reading Frontend.BaseUrl aren't polluted by this marker value.
            await client.PostAsync("/api/v1/platform/settings/Frontend.BaseUrl/reset", null);
        }
    }

    [Fact]
    public async Task UpdateSetting_RequiresConfirmationWithoutPhrase_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // The confirmation check fails before any write happens, so Jwt.SigningKey is never
        // rotated here and there is nothing to reset afterward.
        var response = await client.PutAsJsonAsync("/api/v1/platform/settings/Jwt.SigningKey", new { value = "a-new-signing-key", confirmationPhrase = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResetSetting_PreviouslyUpdated_ReturnsNoContentAndClearsOverride()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await client.PutAsJsonAsync("/api/v1/platform/settings/Billing.GraceDays", new { value = "14", confirmationPhrase = (string?)null });

        var resetResponse = await client.PostAsync("/api/v1/platform/settings/Billing.GraceDays/reset", null);

        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);
        var getResponse = await client.GetAsync("/api/v1/platform/settings");
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        var setting = body.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "Billing.GraceDays");
        Assert.False(setting.GetProperty("isOverridden").GetBoolean());
    }

    [Fact]
    public async Task GetSettings_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = _factory.CreateClient();
        var suffix = $"nsa-settings-{Guid.NewGuid():N}"[..20];
        var registerRequest = new RegisterOrganizationRequest(
            $"Not Super Admin Settings Org {suffix}", $"nsa-set-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner");
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var registerBody = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registerBody.AccessToken);
        var response = await client.GetAsync("/api/v1/platform/settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
