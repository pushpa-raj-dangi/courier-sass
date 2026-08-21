namespace Courier.Identity.Application;

public interface IOrganizationLookupService
{
    // Null iff no Organization with this id exists. Lets other modules (e.g. Shipments'
    // GenerateAwbAsync, which derives the AWB prefix from the org's Slug) resolve this single
    // field without reaching directly into Identity's EF-mapped Organization table — mirrors
    // Merchants.Application.IMerchantLookupService/Branches.Application.IBranchLookupService's
    // shape (a narrow, single-purpose lookup interface, no DbSet exposed).
    Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
