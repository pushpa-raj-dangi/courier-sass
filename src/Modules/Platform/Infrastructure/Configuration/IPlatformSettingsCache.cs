namespace Courier.Platform.Infrastructure.Configuration;

public interface IPlatformSettingsCache
{
    // Synchronous, in-memory only — called from IPostConfigureOptions<T>.PostConfigure, which
    // cannot be async. Returns null when no override is cached for this ConfigPath (fall back
    // to whatever appsettings/env already bound).
    string? TryGetSecret(string configPath);

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
