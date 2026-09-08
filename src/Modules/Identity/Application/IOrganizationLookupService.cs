namespace Courier.Identity.Application;

public interface IOrganizationLookupService
{
    // Null iff no Organization with this id exists. Lets other modules (e.g. Shipments'
    // GenerateAwbAsync, which derives the AWB prefix from the org's Slug) resolve this single
    // field without reaching directly into Identity's EF-mapped Organization table — mirrors
    // Merchants.Application.IMerchantLookupService/Branches.Application.IBranchLookupService's
    // shape (a narrow, single-purpose lookup interface, no DbSet exposed).
    Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default);

    // False for a suspended (IsActive == false) or soft-deleted (DeletedAt is not null)
    // organization, or one that does not exist. Used by every login/auth surface (staff
    // AuthService, MerchantAuthService, ApiKeyAuthenticationHandler) to reject access for a
    // suspended tenant without any of those modules reaching directly into Identity's
    // EF-mapped Organization table.
    Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default);

    // Called by Billing.Application.PaymentService (Task 6) after a payment is confirmed paid.
    // Deliberately a no-op (not an error) if the org isn't suspended, or was suspended manually
    // by a SuperAdmin (SuspendedBySystem == false) — paying an overdue invoice must never
    // silently un-suspend an org a human operator suspended for an unrelated cause.
    Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
