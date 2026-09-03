using Courier.Branches.Application;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Branches.Presentation;

public static class MerchantBranchesEndpoints
{
    public static void MapMerchantBranchesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchant/branches").WithTags("Merchant Branches");

        group.MapGet("/", async (BranchService branchService, CancellationToken cancellationToken) =>
            Results.Ok(await branchService.ListBranchesForMerchantAsync(cancellationToken)))
            .RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.BranchesRead);
    }
}
