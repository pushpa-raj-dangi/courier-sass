using Courier.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class TenantScopeFactoryTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddScoped<TenantScopeOverride>();
        services.AddScoped<ICurrentTenantProvider, HttpContextCurrentTenantProvider>();
        services.AddSingleton<ITenantScopeFactory, TenantScopeFactory>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CreateScope_ProviderInThatScopeReturnsTheGivenOrganization()
    {
        await using var provider = BuildProvider();
        var factory = provider.GetRequiredService<ITenantScopeFactory>();
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();

        await using var scopeA = factory.CreateScope(organizationA);
        await using var scopeB = factory.CreateScope(organizationB);

        Assert.Equal(organizationA, scopeA.ServiceProvider.GetRequiredService<ICurrentTenantProvider>().OrganizationId);
        Assert.Equal(organizationB, scopeB.ServiceProvider.GetRequiredService<ICurrentTenantProvider>().OrganizationId);
    }

    [Fact]
    public async Task PlainScope_WithoutHttpContextOrOverride_HasNoOrganization()
    {
        await using var provider = BuildProvider();

        await using var scope = provider.CreateAsyncScope();

        Assert.Null(scope.ServiceProvider.GetRequiredService<ICurrentTenantProvider>().OrganizationId);
    }
}
