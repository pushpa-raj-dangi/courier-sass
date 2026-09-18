using Microsoft.Extensions.Configuration;

namespace Courier.Platform.Infrastructure.Configuration;

// One call refreshes both halves of the mechanism together — the non-secret IConfiguration
// provider (Task 3) and the secret cache (this file) — so IOptionsMonitor<T> consumers see a
// consistent view after a single reload, whether the changed field was secret or not.
public static class PlatformSettingsReloader
{
    public static async Task ReloadAsync(IConfiguration configuration, IPlatformSettingsCache cache, CancellationToken cancellationToken = default)
    {
        if (configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers.OfType<PlatformSettingsConfigurationProvider>())
            {
                provider.Reload();
            }
        }
        await cache.RefreshAsync(cancellationToken);
    }
}
