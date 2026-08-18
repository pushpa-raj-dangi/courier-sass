namespace Courier.Geography.Application;

public record CreateDeliveryZoneRequest(string Name, List<Guid> CoveredAreaIds);

public record UpdateDeliveryZoneRequest(string Name, List<Guid> CoveredAreaIds);

public record DeliveryZoneResponse(Guid Id, string Name, IReadOnlyList<Guid> CoveredAreaIds);

public class DeliveryZoneValidationException : Exception
{
    public DeliveryZoneValidationException(string message) : base(message) { }
}
