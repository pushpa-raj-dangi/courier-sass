using Courier.SharedKernel;

namespace Courier.Identity.Application;

public record OrganizationRecord(Guid Id, string Name, string Slug, bool IsActive, bool SuspendedBySystem, DateTimeOffset? DeletedAt, DateTimeOffset CreatedAt);
public record OrganizationUserRecord(Guid Id, string Email, string FullName, bool IsActive);

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

    // Cross-tenant read for Platform's OrganizationLifecycleService (SuperAdmin org list) — always
    // called with ICurrentTenantProvider.OrganizationId == null, so the implementation must
    // IgnoreQueryFilters() (Organizations carries a tenant query filter that otherwise fails
    // closed and would return nothing here). Paginated: the base org query is filtered/paged
    // BEFORE OrganizationLifecycleService's per-row plan/branch-count enrichment runs, which is
    // what keeps that enrichment loop bounded to pageSize rows instead of the whole table.
    // search matches (case-insensitive substring): organization Name, Slug, or any User's Email
    // within that organization. isActive/isDeleted map directly to Organization's own fields.
    Task<PagedResult<OrganizationRecord>> ListAsync(string? search, bool? isActive, bool? isDeleted, int? page, int? pageSize, CancellationToken cancellationToken = default);

    // The four lifecycle mutations below are all called only by Platform's
    // OrganizationLifecycleService (SuperAdmin-only, cross-tenant) — same IgnoreQueryFilters()
    // requirement as ListAsync above applies to their shared lookup helper.
    Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default);

    // Deliberately does NOT set IsActive = true — restoring a soft-deleted org leaves it
    // inactive; a separate explicit ReactivateAsync call is required to bring it back online.
    Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default);

    // Cross-tenant read for Platform's PlatformDashboardService (SuperAdmin org detail
    // drill-down) — always called with ICurrentTenantProvider.OrganizationId == null.
    Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default);

    // Cross-tenant read for Platform's PlatformDashboardService (org detail Sheet's Users
    // section) — same caller/context as GetAsync above. IgnoreQueryFilters() is required since
    // Users also carries a tenant query filter that otherwise fails closed against Platform's
    // always-null ambient tenant. Paginated for the same reason ListAsync above is — an org can
    // have far more users than fit in one Sheet.
    Task<PagedResult<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default);

    // Cross-tenant read for Platform's ImpersonationService (SuperAdmin issuing a time-boxed
    // impersonation token for a specific user) — same IgnoreQueryFilters()/explicit organizationId
    // equality requirement as GetAsync/ListUsersAsync above (Platform's ambient tenant is always
    // null). Returns null if the user does not exist OR does not belong to this organization —
    // callers must not distinguish those two cases in any user-facing error.
    Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);

    // Called only by Billing.Application.OverdueInvoiceJob (Task 13), a Hangfire recurring job
    // with no ambient tenant context. Distinct from SuspendAsync (a human SuperAdmin's manual
    // action) — this is the system's own action, audited with a null actor UserId to make "the
    // system, not a person, did this" legible in the audit trail per CLAUDE.md §39. Idempotent:
    // a no-op if the org is already inactive (suspended by this job on a prior run, or manually).
    Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
