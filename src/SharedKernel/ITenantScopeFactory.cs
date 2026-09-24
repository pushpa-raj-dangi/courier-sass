using Microsoft.Extensions.DependencyInjection;

namespace Courier.SharedKernel;

public interface ITenantScopeFactory
{
    // A new DI scope whose ICurrentTenantProvider (and therefore every tenant query filter on
    // the scope's AppDbContext) resolves to organizationId. The caller disposes it.
    AsyncServiceScope CreateScope(Guid organizationId);
}

public sealed class TenantScopeFactory : ITenantScopeFactory
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public TenantScopeFactory(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    public AsyncServiceScope CreateScope(Guid organizationId)
    {
        var scope = _serviceScopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantScopeOverride>().OrganizationId = organizationId;
        return scope;
    }
}
