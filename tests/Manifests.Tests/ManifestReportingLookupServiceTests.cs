using Courier.Geography.Infrastructure;
using Courier.Manifests.Application;
using Courier.Manifests.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Manifests.Tests;

public class ManifestReportingLookupServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId { get; set; }
    }

    private static readonly Guid ProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");

    private static async Task<(Guid OrgId, Guid FromBranchId, Guid ToBranchId)> SeedOrgAsync(Courier.Persistence.AppDbContext dbContext)
    {
        var orgId = Guid.NewGuid();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        dbContext.Add(new Courier.Identity.Domain.Organization { Id = orgId, Name = $"Org {orgId}", Slug = $"org-{orgId:N}" });
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = fromBranchId, OrganizationId = orgId, Code = "FRM", Name = "From Branch",
            Type = Courier.Branches.Domain.BranchType.Hub, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = toBranchId, OrganizationId = orgId, Code = "TO", Name = "To Branch",
            Type = Courier.Branches.Domain.BranchType.Branch, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        await dbContext.SaveChangesAsync();
        return (orgId, fromBranchId, toBranchId);
    }

    [Fact]
    public async Task GetBranchManifestCountsAsync_CountsInTransitAndRecentlyClosed()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, fromBranchId, toBranchId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var inTransit = new Manifest { Id = Guid.NewGuid(), OrganizationId = orgId, Code = "MNF001", FromBranchId = fromBranchId, ToBranchId = toBranchId, Purpose = ManifestPurpose.Forward, Status = ManifestStatus.InTransit, ShipmentCount = 1, CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var closedRecent = new Manifest { Id = Guid.NewGuid(), OrganizationId = orgId, Code = "MNF002", FromBranchId = fromBranchId, ToBranchId = toBranchId, Purpose = ManifestPurpose.Forward, Status = ManifestStatus.Closed, ShipmentCount = 1, CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        dbContext.Add(inTransit);
        dbContext.Add(closedRecent);
        dbContext.Add(new ManifestShipment { Id = Guid.NewGuid(), ManifestId = closedRecent.Id, ShipmentId = Guid.NewGuid(), ScannedAt = DateTimeOffset.UtcNow.AddDays(-2), ReceivedAt = DateTimeOffset.UtcNow.AddHours(-1) });
        await dbContext.SaveChangesAsync();

        var service = new ManifestReportingLookupService(dbContext);
        var result = await service.GetBranchManifestCountsAsync(toBranchId);

        Assert.Equal(1, result.InTransitTowardBranch);
        Assert.Equal(1, result.RecentlyClosed);
    }

    [Fact]
    public async Task GetBranchManifestCountsAsync_ExcludesAnotherOrganizationsManifests()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgAId, fromA, toA) = await SeedOrgAsync(dbContext);
        var (orgBId, fromB, toB) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgAId;

        dbContext.Add(new Manifest { Id = Guid.NewGuid(), OrganizationId = orgAId, Code = "A001", FromBranchId = fromA, ToBranchId = toA, Purpose = ManifestPurpose.Forward, Status = ManifestStatus.InTransit, ShipmentCount = 1, CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow });
        dbContext.Add(new Manifest { Id = Guid.NewGuid(), OrganizationId = orgBId, Code = "B001", FromBranchId = fromB, ToBranchId = toA, Purpose = ManifestPurpose.Forward, Status = ManifestStatus.InTransit, ShipmentCount = 1, CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow });
        await dbContext.SaveChangesAsync();

        var service = new ManifestReportingLookupService(dbContext);
        var result = await service.GetBranchManifestCountsAsync(toA);

        Assert.Equal(1, result.InTransitTowardBranch);
    }

    [Fact]
    public async Task GetOpenMissingShipmentExceptionCountAsync_CountsUnreceivedRowsOnClosedManifests()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, fromBranchId, toBranchId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var closed = new Manifest { Id = Guid.NewGuid(), OrganizationId = orgId, Code = "MNF003", FromBranchId = fromBranchId, ToBranchId = toBranchId, Purpose = ManifestPurpose.Forward, Status = ManifestStatus.Closed, ShipmentCount = 2, CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Add(closed);
        dbContext.Add(new ManifestShipment { Id = Guid.NewGuid(), ManifestId = closed.Id, ShipmentId = Guid.NewGuid(), ScannedAt = DateTimeOffset.UtcNow, ReceivedAt = DateTimeOffset.UtcNow });
        dbContext.Add(new ManifestShipment { Id = Guid.NewGuid(), ManifestId = closed.Id, ShipmentId = Guid.NewGuid(), ScannedAt = DateTimeOffset.UtcNow, ReceivedAt = null });
        await dbContext.SaveChangesAsync();

        var service = new ManifestReportingLookupService(dbContext);
        var count = await service.GetOpenMissingShipmentExceptionCountAsync(toBranchId);

        Assert.Equal(1, count);
    }
}
