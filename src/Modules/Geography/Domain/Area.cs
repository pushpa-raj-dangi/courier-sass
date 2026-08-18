namespace Courier.Geography.Domain;

public class Area
{
    public Guid Id { get; set; }
    public Guid WardId { get; set; }
    public required string Name { get; set; }
}
