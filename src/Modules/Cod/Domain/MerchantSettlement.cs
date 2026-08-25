namespace Courier.Cod.Domain;

// Created by Cod.Application.CodService.CreateMerchantSettlementAsync (Task 9) — on-demand only,
// no scheduled generation (Hangfire/background-job infrastructure is Phase 11's, per
// CLARIFICATIONS.md #9's Ambiguity 4 precedent for AutoRTO). PeriodFrom/PeriodTo use
// DateTimeOffset, matching this codebase's existing convention for period-like fields
// (Pricing.Domain.RateCard.EffectiveFrom/EffectiveTo).
public class MerchantSettlement
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid MerchantId { get; set; }
    public DateTimeOffset PeriodFrom { get; set; }
    public DateTimeOffset PeriodTo { get; set; }
    public decimal TotalPayable { get; set; }
    public MerchantSettlementStatus Status { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
}
