using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class MerchantBranchesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public MerchantBranchesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static CreateBranchRequest ValidBranchRequest(string code) => new(
        code, $"Branch {code}", BranchType.Hub,
        KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null);

    private async Task<HttpClient> RegisterOrgClientAsync(string label)
    {
        var client = _factory.CreateClient();
        // Guid.NewGuid().ToString("N")[..12] (only the GUID segment truncated), not
        // $"{label}-{guid}"[..12] — truncating the whole combined string can drop the GUID's
        // randomness entirely for labels 12+ characters long, causing slug/email collisions
        // against another call in the same test run (this project's own established gotcha).
        var suffix = $"{label}-{Guid.NewGuid().ToString("N")[..12]}";
        var request = new RegisterOrganizationRequest(
            $"MerchBranch Org {suffix}", $"merch-branch-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private async Task<HttpClient> CreateMerchantOwnerClientAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"owner-{label}-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            "Merchant Branches Pvt. Ltd.", "Merchant Branches Co.", "01-4444444", "contact@merchantbranches.example",
            ownerEmail, "P@ssw0rd123!", "Merchant Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");
        var createResponse = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        createResponse.EnsureSuccessStatusCode();

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, "P@ssw0rd123!"));
        loginResponse.EnsureSuccessStatusCode();
        var auth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return merchantClient;
    }

    [Fact]
    public async Task ListBranches_MerchantToken_ReturnsOnlyOwnOrgsActiveBranches()
    {
        var staffClientA = await RegisterOrgClientAsync("merchbranch-a");
        var branchAResponse = await staffClientA.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"MBA-{Guid.NewGuid():N}"[..10]));
        branchAResponse.EnsureSuccessStatusCode();
        var branchA = await branchAResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var staffClientB = await RegisterOrgClientAsync("merchbranch-b");
        var branchBResponse = await staffClientB.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"MBB-{Guid.NewGuid():N}"[..10]));
        branchBResponse.EnsureSuccessStatusCode();
        var branchB = await branchBResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var merchantClient = await CreateMerchantOwnerClientAsync(staffClientA, "merchbranch-a");

        var response = await merchantClient.GetAsync("/api/v1/merchant/branches");

        response.EnsureSuccessStatusCode();
        var branches = await response.Content.ReadFromJsonAsync<List<BranchResponse>>();
        Assert.Contains(branches!, b => b.Id == branchA!.Id);
        Assert.DoesNotContain(branches!, b => b.Id == branchB!.Id);
    }

    [Fact]
    public async Task ListBranches_UnscopedApiKey_ReturnsForbidden()
    {
        var staffClient = await RegisterOrgClientAsync("merchbranch-apikey");
        var merchantClient = await CreateMerchantOwnerClientAsync(staffClient, "merchbranch-apikey");
        var keyResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/api-keys",
            new CreateApiKeyRequest(new[] { MerchantPermissionCatalog.ShipmentRead })); // no BranchesRead
        keyResponse.EnsureSuccessStatusCode();
        var apiKey = await keyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var apiKeyClient = _factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey!.PlaintextKey);

        var response = await apiKeyClient.GetAsync("/api/v1/merchant/branches");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListBranches_NoToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/merchant/branches");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
