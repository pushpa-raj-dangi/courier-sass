using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class HealthPingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthPingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_HealthPing_Returns200Ok()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
