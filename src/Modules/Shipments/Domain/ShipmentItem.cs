namespace Courier.Shipments.Domain;

public class ShipmentItem
{
    public Guid Id { get; set; }
    public Guid ShipmentId { get; set; }
    public required string Description { get; set; }
    public int Quantity { get; set; }
    public decimal UnitValue { get; set; }
}
