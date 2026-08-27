using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class ApiKeyEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public ApiKeyEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> RegisterOrgAndGetStaffTokenAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12];
        var request = new RegisterOrganizationRequest(
            $"Merchant Org {suffix}", $"merchant-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        return auth.AccessToken;
    }

    private async Task<(string OwnerEmail, string Password)> CreateMerchantAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"owner-{label}-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            "Acme Traders Pvt. Ltd.", "Acme Traders", "01-4444444", "contact@acme.example",
            ownerEmail, "P@ssw0rd123!", "Merchant Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        return (ownerEmail, "P@ssw0rd123!");
    }

    private async Task<HttpClient> CreateOwnerClientAsync(string label)
    {
        var staffClient = _factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, label);
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (ownerEmail, ownerPassword) = await CreateMerchantAsync(staffClient, label);

        var ownerClient = _factory.CreateClient();
        var ownerLogin = await ownerClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, ownerPassword));
        ownerLogin.EnsureSuccessStatusCode();
        var ownerAuth = await ownerLogin.Content.ReadFromJsonAsync<MerchantAuthResult>();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth!.AccessToken);
        return ownerClient;
    }

    private async Task<HttpClient> CreateStaffClientAsync(HttpClient ownerClient, string label)
    {
        var staffEmail = $"merchant-staff-{label}-{Guid.NewGuid():N}@example.com";
        var inviteResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/staff",
            new InviteMerchantStaffRequest(staffEmail, "P@ssw0rd123!", "Staff Person"));
        inviteResponse.EnsureSuccessStatusCode();

        var staffLoginClient = _factory.CreateClient();
        var staffLogin = await staffLoginClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(staffEmail, "P@ssw0rd123!"));
        staffLogin.EnsureSuccessStatusCode();
        var staffAuth = await staffLogin.Content.ReadFromJsonAsync<MerchantAuthResult>();
        staffLoginClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffAuth!.AccessToken);
        return staffLoginClient;
    }

    [Fact]
    public async Task OwnerJwt_CreateApiKey_Returns201WithPlaintextKeyMatchingPrefix()
    {
        var ownerClient = await CreateOwnerClientAsync("a");

        var response = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        Assert.NotNull(created);
        Assert.NotEmpty(created!.PlaintextKey);
        var parts = created.PlaintextKey.Split('.', 2);
        Assert.Equal(2, parts.Length);
        Assert.Equal(created.Prefix, parts[0]);
    }

    [Fact]
    public async Task OwnerJwt_ListApiKeys_ContainsCreatedKeyAndNeverLeaksHashedKey()
    {
        var ownerClient = await CreateOwnerClientAsync("b");
        var createResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var listResponse = await ownerClient.GetAsync("/api/v1/merchant/api-keys");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var rawBody = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("hashedKey", rawBody, StringComparison.OrdinalIgnoreCase);

        var keys = await listResponse.Content.ReadFromJsonAsync<List<ApiKeyResponse>>();
        Assert.Contains(keys!, k => k.Id == created!.Id);
    }

    [Fact]
    public async Task OwnerJwt_DeleteApiKey_RevokesIt()
    {
        var ownerClient = await CreateOwnerClientAsync("c");
        var createResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var deleteResponse = await ownerClient.DeleteAsync($"/api/v1/merchant/api-keys/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        var revoked = await deleteResponse.Content.ReadFromJsonAsync<ApiKeyResponse>();
        Assert.False(revoked!.IsActive);
        Assert.NotNull(revoked.RevokedAt);
    }

    [Fact]
    public async Task StaffJwt_CreateApiKey_Returns403()
    {
        var ownerClient = await CreateOwnerClientAsync("d");
        var staffClient = await CreateStaffClientAsync(ownerClient, "d");

        var response = await staffClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NoAuth_CreateApiKey_Returns401()
    {
        var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
