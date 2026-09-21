using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class FxRateEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public FxRateEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> SuperAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString()!);
        return client;
    }

    [Fact]
    public async Task List_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/platform/fx-rates");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Record_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/platform/fx-rates", new { fromCurrency = "USD", toCurrency = "NPR", rate = 133m });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Record_AsSuperAdmin_CreatesRateAndListsIt()
    {
        var client = await SuperAdminClientAsync();

        // FxRates is append-only by design, so this row cannot be cleaned up afterwards. ISO 4217
        // XTS is the code reserved for testing, so it can never collide with a real pair.
        var rate = 100m + (decimal)(Random.Shared.Next(1, 90000)) / 1000m;
        var post = await client.PostAsJsonAsync("/api/v1/platform/fx-rates", new { fromCurrency = "xts", toCurrency = "NPR", rate });

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var created = await post.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("XTS", created.GetProperty("fromCurrency").GetString());

        var list = await client.GetAsync("/api/v1/platform/fx-rates?from=XTS&to=NPR&page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var body = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(created.GetProperty("id").GetString(), body.GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("USD", "USD", 1)]
    [InlineData("US", "NPR", 1)]
    [InlineData("USD", "NPR", 0)]
    public async Task Record_InvalidInput_ReturnsBadRequestWithErrorContract(string from, string to, double rate)
    {
        var client = await SuperAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/platform/fx-rates", new { fromCurrency = from, toCurrency = to, rate });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FX_RATE_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }
}
