using Courier.Cod.Domain;

namespace Courier.Cod.Application;

public record CodCollectionResponse(
    Guid Id, Guid ShipmentId, Guid MerchantId, Guid BranchId, decimal ExpectedAmount, decimal ShipmentTotalCharge,
    decimal? CollectedAmount, CodCollectionStatus Status, Guid? CollectedByRiderId, DateTimeOffset? CollectedAt,
    Guid? DeliveryProofId, Guid? RiderCashSettlementId, Guid? MerchantSettlementItemId);

public record CreateRiderSettlementRequest(Guid RiderId, Guid BranchId, decimal DeclaredAmount);

public record RiderCashSettlementResponse(
    Guid Id, Guid RiderId, Guid BranchId, decimal DeclaredAmount, int ShipmentCount,
    DateTimeOffset SettledAt, Guid ConfirmedByUserId, Guid? BranchCashSettlementId);

public class CodValidationException : Exception
{
    public CodValidationException(string message) : base(message) { }
}
