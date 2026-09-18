namespace Courier.Platform.Domain;

// Static catalog of platform-wide settings a SuperAdmin can override at runtime — rows are
// seeded via migration (PlatformSettingCatalog.Definitions), never created through the API.
public class PlatformSettingDefinition
{
    public string Key { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string ConfigPath { get; set; } = default!;
    public string ValueType { get; set; } = default!;
    public bool IsSecret { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string? Description { get; set; }
}
