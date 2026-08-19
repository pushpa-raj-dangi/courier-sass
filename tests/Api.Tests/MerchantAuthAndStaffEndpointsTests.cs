using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class MerchantAuthAndStaffEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public MerchantAuthAndStaffEndpointsTests(WebApplicationFactory<Program> factory)
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

    [Fact]
    public async Task MerchantOwnerLogin_ThenGetProfile_Succeeds()
    {
        var staffClient = _factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, "a");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (ownerEmail, password) = await CreateMerchantAsync(staffClient, "a");

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var auth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();

        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        var profileResponse = await merchantClient.GetAsync("/api/v1/merchant/profile");
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        var profile = await profileResponse.Content.ReadFromJsonAsync<MerchantResponse>();
        Assert.Equal("Acme Traders", profile!.DisplayName);
    }

    [Fact]
    public async Task MerchantOwnerLogin_WrongPassword_Returns401()
    {
        var staffClient = _factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, "b");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (ownerEmail, _) = await CreateMerchantAsync(staffClient, "b");

        var merchantClient = _factory.CreateClient();
        var response = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Owner_InvitesStaff_StaffCanLoginAndReadProfileButNotInviteOthers()
    {
        var staffClient = _factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, "c");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (ownerEmail, ownerPassword) = await CreateMerchantAsync(staffClient, "c");

        var ownerClient = _factory.CreateClient();
        var ownerLogin = await ownerClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, ownerPassword));
        var ownerAuth = await ownerLogin.Content.ReadFromJsonAsync<MerchantAuthResult>();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth!.AccessToken);

        var staffEmail = $"merchant-staff-{Guid.NewGuid():N}@example.com";
        var inviteResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/staff",
            new InviteMerchantStaffRequest(staffEmail, "P@ssw0rd123!", "Staff Person"));
        Assert.Equal(HttpStatusCode.Created, inviteResponse.StatusCode);

        var staffLoginClient = _factory.CreateClient();
        var staffLogin = await staffLoginClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(staffEmail, "P@ssw0rd123!"));
        var staffAuth = await staffLogin.Content.ReadFromJsonAsync<MerchantAuthResult>();
        staffLoginClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffAuth!.AccessToken);

        var staffProfileResponse = await staffLoginClient.GetAsync("/api/v1/merchant/profile");
        Assert.Equal(HttpStatusCode.OK, staffProfileResponse.StatusCode);

        var staffInviteAttempt = await staffLoginClient.PostAsJsonAsync("/api/v1/merchant/staff",
            new InviteMerchantStaffRequest($"blocked-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Blocked"));
        Assert.Equal(HttpStatusCode.Forbidden, staffInviteAttempt.StatusCode);
    }
}
