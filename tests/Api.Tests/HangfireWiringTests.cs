using Hangfire;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Xunit;

namespace Courier.Api.Tests;

public class HangfireWiringTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HangfireWiringTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void RabbitMqConnection_ResolvesAsSingleton()
    {
        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();
        var connection1 = scope1.ServiceProvider.GetRequiredService<IConnection>();
        var connection2 = scope2.ServiceProvider.GetRequiredService<IConnection>();

        Assert.True(connection1.IsOpen);
        Assert.Same(connection1, connection2);
    }

    [Fact]
    public void HangfireBackgroundJobClient_Resolves()
    {
        using var scope = _factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
        Assert.NotNull(client);
    }
}
