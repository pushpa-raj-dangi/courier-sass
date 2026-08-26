using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.Webhooks.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class WebhooksEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public WebhooksEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> RegisterOrgAndGetStaffTokenAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12];
        var request = new RegisterOrganizationRequest(
            $"Webhooks Org {suffix}", $"webhooks-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        return auth.AccessToken;
    }

    private async Task<(string OwnerEmail, string Password)> CreateMerchantAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"owner-{label}-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            "Webhook Traders Pvt. Ltd.", "Webhook Traders", "01-4444444", "contact@webhooktraders.example",
            ownerEmail, "P@ssw0rd123!", "Merchant Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        return (ownerEmail, "P@ssw0rd123!");
    }

    private async Task<HttpClient> CreateAuthenticatedMerchantClientAsync(string label)
    {
        var staffClient = _factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, label);
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (ownerEmail, password) = await CreateMerchantAsync(staffClient, label);

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, password));
        loginResponse.EnsureSuccessStatusCode();
        var auth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return merchantClient;
    }

    [Fact]
    public async Task CreateAndListAndDeleteSubscription_FullLifecycle()
    {
        var client = await CreateAuthenticatedMerchantClientAsync("lifecycle");

        var createResponse = await client.PostAsJsonAsync("/api/v1/webhooks", new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "shipment.created" }));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateWebhookSubscriptionResponse>();
        Assert.NotNull(created);
        Assert.NotEmpty(created!.Secret);

        var listResponse = await client.GetAsync("/api/v1/webhooks");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<List<WebhookSubscriptionResponse>>();
        Assert.Single(list!, s => s.Id == created.Id);

        var deleteResponse = await client.DeleteAsync($"/api/v1/webhooks/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listAfterDelete = await client.GetAsync("/api/v1/webhooks");
        var listAfter = await listAfterDelete.Content.ReadFromJsonAsync<List<WebhookSubscriptionResponse>>();
        Assert.Empty(listAfter!);
    }

    [Fact]
    public async Task CreateSubscription_WithoutMerchantAuthentication_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/webhooks", new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "shipment.created" }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
