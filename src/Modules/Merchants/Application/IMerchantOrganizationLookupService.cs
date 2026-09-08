namespace Courier.Merchants.Application;

// Deliberately not a direct ProjectReference to Identity.Application.IOrganizationLookupService:
// Branches.csproj already references Merchants.csproj (Branches.Presentation.
// MerchantBranchesEndpoints), and Identity.csproj already references Branches.csproj, so a
// Merchants -> Identity reference would close the cycle Identity -> Branches -> Merchants ->
// Identity. Merchants instead owns this narrow interface itself (same "no DbSet exposed"
// shape as IMerchantLookupService/IBranchLookupService); the Api composition root — which
// already references both Identity and Merchants, and is referenced by neither — supplies the
// real implementation by adapting Identity.Application.IOrganizationLookupService.IsActiveAsync.
public interface IMerchantOrganizationLookupService
{
    Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
