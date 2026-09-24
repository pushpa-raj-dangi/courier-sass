using System.Security.Claims;
using Courier.International.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Application;

// Shared by InternationalShipmentService and InternationalShipmentQueryService.
internal static class InternationalShipmentAccess
{
    // Same staff visibility rule as ShipmentService.ListAsync (ShipmentService.Reads.cs): a
    // branch-restricted caller sees a shipment iff it touches one of their branches as origin,
    // destination, or current physical location; null from the checker means AllBranches (no
    // filter). Callers turn "not visible" into 404, never 403, so an out-of-scope id is
    // indistinguishable from a nonexistent one.
    public static async Task<bool> IsVisibleAsync(
        IBranchAuthorizationChecker branchAuthorizationChecker, ClaimsPrincipal actingUser, string permission,
        Guid originBranchId, Guid destinationBranchId, Guid? currentBranchId, CancellationToken cancellationToken)
    {
        var accessible = await branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, permission, cancellationToken);
        return accessible is null
            || accessible.Contains(originBranchId)
            || accessible.Contains(destinationBranchId)
            || (currentBranchId is Guid current && accessible.Contains(current));
    }

    // CustomsItem has no tenant filter of its own; reaching it only through an already
    // tenant-filtered InternationalLeg's Id is what keeps this read tenant-safe.
    public static async Task<InternationalLegResponse> ToLegResponseAsync(DbContext dbContext, InternationalLeg leg, CancellationToken cancellationToken)
    {
        var items = await dbContext.Set<CustomsItem>()
            .Where(i => i.InternationalLegId == leg.Id)
            .OrderBy(i => i.ShipmentItemId)
            .ToListAsync(cancellationToken);
        return ToLegResponse(leg, items);
    }

    public static InternationalLegResponse ToLegResponse(InternationalLeg leg, IEnumerable<CustomsItem> items) => new(
        leg.ShipmentId, leg.ContentsDescription, leg.CountryOfOrigin, leg.Purpose.ToString(), leg.Incoterm.ToString(),
        leg.ExportLicenceReference, leg.CustomsCompletedAt is not null, leg.ProvisionalTotalChargeAmount, leg.ProvisionalTotalChargeCurrency, leg.IsProvisionalPricing,
        items.Select(i => new CustomsItemResponse(i.ShipmentItemId, i.HsCode, i.CustomsValueAmount, i.CustomsValueCurrency)).ToList());
}
