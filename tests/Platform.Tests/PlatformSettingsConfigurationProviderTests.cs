using Courier.Platform.Infrastructure.Configuration;
using Courier.Platform.Tests;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Courier.Platform.Tests;

public class PlatformSettingsConfigurationProviderTests
{
    [Fact]
    public void Load_NoOverrides_ReturnsEmptyData()
    {
        var provider = new PlatformSettingsConfigurationProvider(TestDbContextFactory.GetConnectionString());

        provider.Load();

        Assert.False(provider.TryGet("Esewa:ProductCode", out _));
    }

    [Fact]
    public void Load_TableMissing_DoesNotThrow()
    {
        // A connection string pointing at a database with no PlatformSettings table yet must
        // resolve to "no overrides" rather than crash — this is the real fresh-install boot
        // path (see PlatformSettingsConfigurationProvider's Load() comment).
        var provider = new PlatformSettingsConfigurationProvider(
            "Host=localhost;Port=5434;Database=postgres;Username=courier;Password=courier_dev_password");

        var exception = Record.Exception(() => provider.Load());

        Assert.Null(exception);
    }
}
