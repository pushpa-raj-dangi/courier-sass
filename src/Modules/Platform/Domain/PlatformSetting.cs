namespace Courier.Platform.Domain;

// One row per PlatformSettingDefinition.Key, created lazily on first override. IsOverridden ==
// false means "not set, the app falls back to appsettings/env" — see
// PlatformSettingsConfigurationProvider for how this fallback is actually resolved.
public class PlatformSetting
{
    public string Key { get; set; } = default!;
    public string? Value { get; set; }
    public bool IsOverridden { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }
}
