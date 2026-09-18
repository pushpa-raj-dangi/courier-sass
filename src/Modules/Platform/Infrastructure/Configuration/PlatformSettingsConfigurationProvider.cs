using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Courier.Platform.Infrastructure.Configuration;

// Non-secret values only — see spec §5. WebApplicationBuilder.Configuration is a
// ConfigurationManager, which runs a newly-Add()ed source's Load() synchronously, immediately,
// before a single builder.Services.Add...() line has resolved anything. No DI container exists
// yet at that point, so this cannot resolve IDataProtectionProvider to decrypt secrets — it opens
// its own plain NpgsqlConnection instead, using the connection string captured at construction.
public class PlatformSettingsConfigurationProvider : ConfigurationProvider
{
    private readonly string _connectionString;

    public PlatformSettingsConfigurationProvider(string connectionString)
    {
        _connectionString = connectionString;
    }

    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            using var command = new NpgsqlCommand(
                "SELECT d.\"ConfigPath\", s.\"Value\" FROM \"PlatformSettings\" s " +
                "JOIN \"PlatformSettingDefinitions\" d ON s.\"Key\" = d.\"Key\" " +
                "WHERE s.\"IsOverridden\" = TRUE AND d.\"IsSecret\" = FALSE",
                connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                data[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // "PlatformSettings"/"PlatformSettingDefinitions" don't exist yet — a brand-new
            // database where migrations (Program.cs) haven't run yet, since they run AFTER
            // builder.Build(), i.e. after this Load() already executed once. Treat as
            // "no overrides configured" rather than crashing startup.
        }
        Data = data;
    }

    // Re-runs Load() and fires the base class's change-token notification — the same mechanism
    // IOptionsMonitor<T> already listens to, so no custom pub/sub-to-options wiring is needed.
    public void Reload()
    {
        Load();
        OnReload();
    }
}
