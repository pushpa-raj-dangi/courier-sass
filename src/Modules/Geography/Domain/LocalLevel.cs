namespace Courier.Geography.Domain;

public class LocalLevel
{
    public Guid Id { get; set; }
    public Guid DistrictId { get; set; }
    public required string Name { get; set; }
    public LocalLevelType Type { get; set; }
}
