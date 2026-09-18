using Courier.Billing.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Microsoft.Extensions.Options;

namespace Courier.Platform.Infrastructure.Configuration;

// Runs lazily, inside IOptionsFactory<T>, only when something resolves
// IOptionsMonitor<T>.CurrentValue — always inside a fully-built DI container, unlike
// PlatformSettingsConfigurationProvider.Load() (Task 3). Overwrites just the one secret
// property after Configure<T>(section) already populated everything else from IConfiguration.

public class EsewaSecretPostConfigureOptions : IPostConfigureOptions<EsewaOptions>
{
    private readonly IPlatformSettingsCache _cache;
    public EsewaSecretPostConfigureOptions(IPlatformSettingsCache cache) => _cache = cache;

    public void PostConfigure(string? name, EsewaOptions options)
    {
        var overrideValue = _cache.TryGetSecret("Esewa:SecretKey");
        if (overrideValue is not null)
        {
            options.SecretKey = overrideValue;
        }
    }
}

public class KhaltiSecretPostConfigureOptions : IPostConfigureOptions<KhaltiOptions>
{
    private readonly IPlatformSettingsCache _cache;
    public KhaltiSecretPostConfigureOptions(IPlatformSettingsCache cache) => _cache = cache;

    public void PostConfigure(string? name, KhaltiOptions options)
    {
        var overrideValue = _cache.TryGetSecret("Khalti:SecretKey");
        if (overrideValue is not null)
        {
            options.SecretKey = overrideValue;
        }
    }
}

public class JwtSecretPostConfigureOptions : IPostConfigureOptions<JwtOptions>
{
    private readonly IPlatformSettingsCache _cache;
    public JwtSecretPostConfigureOptions(IPlatformSettingsCache cache) => _cache = cache;

    public void PostConfigure(string? name, JwtOptions options)
    {
        var overrideValue = _cache.TryGetSecret("Jwt:SigningKey");
        if (overrideValue is not null)
        {
            options.SigningKey = overrideValue;
        }
    }
}

public class MerchantJwtSecretPostConfigureOptions : IPostConfigureOptions<MerchantJwtOptions>
{
    private readonly IPlatformSettingsCache _cache;
    public MerchantJwtSecretPostConfigureOptions(IPlatformSettingsCache cache) => _cache = cache;

    public void PostConfigure(string? name, MerchantJwtOptions options)
    {
        var overrideValue = _cache.TryGetSecret("Jwt:SigningKey");
        if (overrideValue is not null)
        {
            options.SigningKey = overrideValue;
        }
    }
}
