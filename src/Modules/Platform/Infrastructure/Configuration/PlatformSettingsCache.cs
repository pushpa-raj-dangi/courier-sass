using Courier.Platform.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Courier.Platform.Infrastructure.Configuration;

// Singleton. Decrypted secret values (Esewa/Khalti/Jwt SecretKey overrides) live here, refreshed
// by PlatformSettingsChangeListener on startup, on a Redis invalidation message, and on a
// reconciliation timer. IPostConfigureOptions<T> implementations (Task 5) read from this
// synchronously — never touch the database directly on the request path.
public class PlatformSettingsCache : IPlatformSettingsCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlatformSettingsCache> _logger;
    private volatile Dictionary<string, string> _secrets = new();

    public PlatformSettingsCache(IServiceScopeFactory scopeFactory, ILogger<PlatformSettingsCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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
                if (row.Value is null)
                {
                    continue;
                }

                try
                {
                    next[row.ConfigPath] = protector.Unprotect(row.Value);
                }
                catch (Exception ex)
                {
                    // A single row with undecryptable ciphertext (e.g. protected under a key
                    // ring this instance doesn't have, or corrupted data) must not abort the
                    // whole refresh — that would crash the app at boot via
                    // PlatformSettingsChangeListener's synchronous startup reload. Skip it and
                    // keep processing the rest; the previous cached value (if any) for this
                    // ConfigPath is simply not refreshed.
                    _logger.LogWarning(ex, "Failed to decrypt platform setting secret for {ConfigPath}; skipping it in this refresh.", row.ConfigPath);
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
