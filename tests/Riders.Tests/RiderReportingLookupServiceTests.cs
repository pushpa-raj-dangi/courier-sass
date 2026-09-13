using Courier.Geography.Infrastructure;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Tests;

public class RiderReportingLookupServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId { get; set; }
    }

    private static readonly Guid ProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");

    private static async Task<(Guid OrgId, Guid BranchId)> SeedOrgAsync(Courier.Persistence.AppDbContext dbContext)
    {
        var orgId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        dbContext.Add(new Courier.Identity.Domain.Organization { Id = orgId, Name = $"Org {orgId}", Slug = $"org-{orgId:N}" });
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = branchId, OrganizationId = orgId, Code = "BR1", Name = "Branch One",
            Type = Courier.Branches.Domain.BranchType.Branch, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        await dbContext.SaveChangesAsync();
        return (orgId, branchId);
    }

    private static Rider BuildRider(Guid orgId, Guid branchId) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = orgId, UserId = Guid.NewGuid(), BaseBranchId = branchId,
        LicenseNumber = $"LIC{Guid.NewGuid():N}"[..10], RoleType = RiderRoleType.DeliveryRider, IsActive = true
    };

    [Fact]
    public async Task GetBranchRiderTaskLoadAsync_CountsOnlyPendingTasks_ForActiveRidersAtBranch()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var rider = BuildRider(orgId, branchId);
        dbContext.Add(rider);
        var run = new DeliveryRun { Id = Guid.NewGuid(), RiderId = rider.Id, BranchId = branchId, Date = DateOnly.FromDateTime(DateTime.UtcNow), Status = DeliveryRunStatus.Active };
        dbContext.Add(run);
        dbContext.Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = Guid.NewGuid(), Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending });
        dbContext.Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = Guid.NewGuid(), Type = DeliveryTaskType.Delivery, Sequence = 2, Status = DeliveryTaskStatus.Completed });
        await dbContext.SaveChangesAsync();

        var service = new RiderReportingLookupService(dbContext);
        var result = await service.GetBranchRiderTaskLoadAsync(branchId);

        var load = Assert.Single(result);
        Assert.Equal(rider.Id, load.RiderId);
        Assert.Equal(1, load.OpenTaskCount);
    }

    [Fact]
    public async Task GetBranchRiderTaskLoadAsync_ExcludesAnotherOrganizationsRiders()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgAId, branchA) = await SeedOrgAsync(dbContext);
        var (orgBId, branchB) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgAId;

        dbContext.Add(BuildRider(orgAId, branchA));
        dbContext.Add(BuildRider(orgBId, branchA)); // same branch id would never really collide across orgs, but exercise the filter directly
        await dbContext.SaveChangesAsync();

        var service = new RiderReportingLookupService(dbContext);
        var result = await service.GetBranchRiderTaskLoadAsync(branchA);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetSuccessRateByBranchAsync_ComputesAttemptsAndSuccessesInRange()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var rider = BuildRider(orgId, branchId);
        dbContext.Add(rider);
        var now = DateTimeOffset.UtcNow;
        dbContext.Add(new DeliveryAttempt { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = Guid.NewGuid(), RiderId = rider.Id, AttemptNumber = 1, AttemptedAt = now, Succeeded = true, CustomerContacted = true });
        dbContext.Add(new DeliveryAttempt { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = Guid.NewGuid(), RiderId = rider.Id, AttemptNumber = 1, AttemptedAt = now, Succeeded = false, CustomerContacted = true });
        await dbContext.SaveChangesAsync();

        var service = new RiderReportingLookupService(dbContext);
        var result = await service.GetSuccessRateByBranchAsync(null, now.AddHours(-1), now.AddHours(1));

        Assert.Equal(2, result[branchId].Attempts);
        Assert.Equal(1, result[branchId].Successes);
    }

    [Fact]
    public async Task GetSuccessRateByRiderAsync_BucketsPerRiderId()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var rider = BuildRider(orgId, branchId);
        dbContext.Add(rider);
        var now = DateTimeOffset.UtcNow;
        dbContext.Add(new DeliveryAttempt { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = Guid.NewGuid(), RiderId = rider.Id, AttemptNumber = 1, AttemptedAt = now, Succeeded = true, CustomerContacted = true });
        await dbContext.SaveChangesAsync();

        var service = new RiderReportingLookupService(dbContext);
        var result = await service.GetSuccessRateByRiderAsync(null, now.AddHours(-1), now.AddHours(1));

        Assert.Equal(1, result[rider.Id].Attempts);
        Assert.Equal(1, result[rider.Id].Successes);
    }

    [Fact]
    public async Task GetSummaryAsync_CountsActiveInactiveAndCurrentlyAssigned()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var activeAssignedRider = BuildRider(orgId, branchId);
        var inactiveRider = BuildRider(orgId, branchId);
        inactiveRider.IsActive = false;
        dbContext.Add(activeAssignedRider);
        dbContext.Add(inactiveRider);
        var run = new DeliveryRun { Id = Guid.NewGuid(), RiderId = activeAssignedRider.Id, BranchId = branchId, Date = DateOnly.FromDateTime(DateTime.UtcNow), Status = DeliveryRunStatus.Active };
        dbContext.Add(run);
        dbContext.Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = Guid.NewGuid(), Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending });
        await dbContext.SaveChangesAsync();

        var service = new RiderReportingLookupService(dbContext);
        var result = await service.GetSummaryAsync(new HashSet<Guid> { branchId });

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Active);
        Assert.Equal(1, result.Inactive);
        Assert.Equal(1, result.CurrentlyAssigned);
    }
}
