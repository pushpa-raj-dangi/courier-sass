using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.International.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierAccountEndpointsTests : InternationalApiTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public CarrierAccountEndpointsTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private static object ManualAccountBody(Dictionary<string, string>? credentials = null) => new
    {
        carrierCode = "MANUAL",
        displayName = "Manual carrier",
        credentials,
        pollingEnabled = false,
        generateWebhookSecret = false
    };

    [Fact]
    public async Task CreateListUpdate_ManualAccount_NeverReturnsCredentialValues()
    {
        var org = await SetUpOrgAsync("acct-crud");

        var create = await org.Client.PostAsJsonAsync("/api/v1/international/carrier-accounts",
            ManualAccountBody(new Dictionary<string, string> { ["contact"] = "ops-desk-secret-9" }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var createBody = await create.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ops-desk-secret-9", createBody);
        var created = JsonSerializer.Deserialize<CarrierAccountSecretResponse>(createBody, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(created.Account.HasCredentials);
        Assert.Null(created.WebhookSecret);

        var list = await org.Client.GetFromJsonAsync<List<CarrierAccountResponse>>("/api/v1/international/carrier-accounts");
        Assert.Single(list!, a => a.Id == created.Account.Id);

        var update = await org.Client.PutAsJsonAsync($"/api/v1/international/carrier-accounts/{created.Account.Id}",
            new { displayName = "Manual (retired)", isActive = false, pollingEnabled = false });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.False((await update.Content.ReadFromJsonAsync<CarrierAccountResponse>())!.IsActive);
    }

    [Fact]
    public async Task Create_UnknownCarrierCode_Returns400WithAccountValidationCode()
    {
        var org = await SetUpOrgAsync("acct-unknown");

        var response = await org.Client.PostAsJsonAsync("/api/v1/international/carrier-accounts",
            new { carrierCode = "NOPE", displayName = "X", credentials = (object?)null, pollingEnabled = false, generateWebhookSecret = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CARRIER_ACCOUNT_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OtherTenantsAccount_Returns404()
    {
        var owner = await SetUpOrgAsync("acct-owner");
        var other = await SetUpOrgAsync("acct-other");
        var create = await owner.Client.PostAsJsonAsync("/api/v1/international/carrier-accounts", ManualAccountBody());
        var created = (await create.Content.ReadFromJsonAsync<CarrierAccountSecretResponse>())!;

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/v1/international/carrier-accounts/{created.Account.Id}")).StatusCode);
        var update = await other.Client.PutAsJsonAsync($"/api/v1/international/carrier-accounts/{created.Account.Id}",
            new { displayName = "Hijacked", isActive = false, pollingEnabled = false });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
    }

    [Fact]
    public async Task WithoutCarrierManagePermission_Returns403()
    {
        var org = await SetUpOrgAsync("acct-403");
        var client = await SeedUserClientAsync(org.Auth, new[] { "international.read" }, null);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/international/carrier-accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/international/carrier-accounts", ManualAccountBody())).StatusCode);
    }
}
