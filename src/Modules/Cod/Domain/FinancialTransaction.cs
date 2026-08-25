namespace Courier.Cod.Domain;

// Created by Cod.Application.CodService.CreateFinanceVerificationAsync (Task 8) — one row per
// BranchCashSettlement verified. ReferenceEntityType/ReferenceEntityId point at the
// BranchCashSettlement this verification covers (generic reference-entity pair, matching this
// entity's DOMAIN-MODEL.md shape, kept extensible even though FinancialTransactionType has a single
// member this phase).
public class FinancialTransaction
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public FinancialTransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public required string ReferenceEntityType { get; set; }
    public Guid ReferenceEntityId { get; set; }
    public Guid VerifiedByUserId { get; set; }
    public DateTimeOffset VerifiedAt { get; set; }
}
