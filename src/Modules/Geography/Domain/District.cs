namespace Courier.Geography.Domain;

public class District
{
    public Guid Id { get; set; }
    public Guid ProvinceId { get; set; }
    public required string Name { get; set; }
}
