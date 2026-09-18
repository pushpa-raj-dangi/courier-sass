using Courier.Platform.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Courier.Platform.Infrastructure.Configuration;

// Singleton. Decrypted secret values (Esewa/Khalti/Jwt SecretKey overrides) live here, refreshed
// by PlatformSettingsChangeListener on startup, on a Redis invalidation message, and on a
// reconciliation timer. IPostConfigureOptions<T> implementations (Task 5) read from this
// synchronously — never touch the database directly on the request path.
public class PlatformSettingsCache : IPlatformSettingsCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private volatile Dictionary<string, string> _secrets = new();

    public PlatformSettingsCache(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public string? TryGetSecret(string configPath) => _secrets.TryGetValue(configPath, out var value) ? value : null;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var dataProtectionProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var protector = dataProtectionProvider.CreateProtector("PlatformSettings.Secrets");

        var next = new Dictionary<string, string>();
        try
        {
            var rows = await (
                from s in dbContext.Set<PlatformSetting>()
                join d in dbContext.Set<PlatformSettingDefinition>() on s.Key equals d.Key
                where s.IsOverridden && d.IsSecret
                select new { s.Value, d.ConfigPath }
            ).ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (row.Value is not null)
                {
                    next[row.ConfigPath] = protector.Unprotect(row.Value);
                }
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Same fresh-install race as PlatformSettingsConfigurationProvider — this service
            // starts after migrations run in this codebase's own startup order, but a production
            // deploy that starts the app before its separate migration step must not crash here.
        }
        _secrets = next;
    }
}
