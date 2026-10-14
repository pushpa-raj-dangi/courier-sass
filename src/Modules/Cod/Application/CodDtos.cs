using Courier.Cod.Domain;

namespace Courier.Cod.Application;

public record CodCollectionResponse(
    Guid Id, Guid ShipmentId, Guid MerchantId, Guid BranchId, decimal ExpectedAmount, decimal ShipmentTotalCharge,
    decimal? CollectedAmount, CodCollectionStatus Status, Guid? CollectedByRiderId, DateTimeOffset? CollectedAt,
    Guid? DeliveryProofId, Guid? RiderCashSettlementId, Guid? MerchantSettlementItemId, DateTimeOffset CreatedAt);

public record CreateRiderSettlementRequest(Guid RiderId, Guid BranchId, decimal DeclaredAmount);

public record RiderCashSettlementResponse(
    Guid Id, Guid RiderId, Guid BranchId, decimal DeclaredAmount, int ShipmentCount,
    DateTimeOffset SettledAt, Guid ConfirmedByUserId, Guid? BranchCashSettlementId,
    decimal ExpectedAmount, decimal Variance);

public record CreateBranchSettlementRequest(Guid BranchId, List<Guid> RiderCashSettlementIds, decimal ConfirmedAmount);

public record BranchCashSettlementResponse(
    Guid Id, Guid BranchId, decimal ConfirmedAmount, Guid ReconciledByUserId, DateTimeOffset ReconciledAt,
    List<Guid> RiderCashSettlementIds);

public record CreateFinanceVerificationRequest(Guid BranchCashSettlementId, decimal Amount);

public record FinancialTransactionResponse(
    Guid Id, FinancialTransactionType Type, decimal Amount, string ReferenceEntityType, Guid ReferenceEntityId,
    Guid VerifiedByUserId, DateTimeOffset VerifiedAt);

public record CreateMerchantSettlementRequest(Guid MerchantId, DateTimeOffset PeriodFrom, DateTimeOffset PeriodTo);

public record MerchantSettlementItemResponse(Guid Id, Guid ShipmentId, string ShipmentAwb, decimal CODAmount, decimal CourierFee, decimal OtherDeductions, decimal NetPayable);

public record MerchantSettlementResponse(
    Guid Id, Guid MerchantId, DateTimeOffset PeriodFrom, DateTimeOffset PeriodTo, decimal TotalPayable,
    MerchantSettlementStatus Status, DateTimeOffset? SettledAt, List<MerchantSettlementItemResponse> Items);

public class CodValidationException : Exception
{
    public CodValidationException(string message) : base(message) { }
}

public record RequestAdjustmentRequest(string EntityType, Guid EntityId, decimal AfterValue, string Reason);

public record AdjustmentResponse(
    Guid Id, string EntityType, Guid EntityId, decimal BeforeValue, decimal AfterValue, string Reason,
    Guid RequestedByUserId, DateTimeOffset RequestedAt, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt, bool Applied);

public record CodReconciliationSummaryResponse(
    decimal ExpectedCod, decimal CollectedCod, decimal RiderDepositedCod, decimal BranchReceivedCod,
    decimal MerchantPayableCod, decimal TotalCourierFees);
