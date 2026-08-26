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

public record CreateBranchSettlementRequest(Guid BranchId, List<Guid> RiderCashSettlementIds, decimal ConfirmedAmount);

public record BranchCashSettlementResponse(
    Guid Id, Guid BranchId, decimal ConfirmedAmount, Guid ReconciledByUserId, DateTimeOffset ReconciledAt,
    List<Guid> RiderCashSettlementIds);

public record CreateFinanceVerificationRequest(Guid BranchCashSettlementId, decimal Amount);

public record FinancialTransactionResponse(
    Guid Id, FinancialTransactionType Type, decimal Amount, string ReferenceEntityType, Guid ReferenceEntityId,
    Guid VerifiedByUserId, DateTimeOffset VerifiedAt);

public class CodValidationException : Exception
{
    public CodValidationException(string message) : base(message) { }
}
