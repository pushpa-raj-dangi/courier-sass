using Microsoft.Extensions.Configuration;

namespace Courier.Platform.Infrastructure.Configuration;

public class PlatformSettingsConfigurationSource : IConfigurationSource
{
    private readonly string _connectionString;

    public PlatformSettingsConfigurationSource(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new PlatformSettingsConfigurationProvider(_connectionString);
}
