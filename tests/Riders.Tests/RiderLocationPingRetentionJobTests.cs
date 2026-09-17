using Courier.Branches.Domain;
using Courier.Geography.Domain;
using Courier.Identity.Domain;
using Courier.Persistence;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Riders.Tests;

public class RiderLocationPingRetentionJobTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    [Fact]
    public async Task RunAsync_PingOlderThan30Days_IsDeleted()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var (riderId, organizationId) = await SetupRiderAsync(dbContext);
        var oldPing = new RiderLocationPing { Id = Guid.NewGuid(), RiderId = riderId, Latitude = 27.7m, Longitude = 85.3m, RecordedAt = DateTimeOffset.UtcNow, ReceivedAt = DateTimeOffset.UtcNow.AddDays(-31) };
        dbContext.Set<RiderLocationPing>().Add(oldPing);
        await dbContext.SaveChangesAsync();
        var job = new RiderLocationPingRetentionJob(dbContext);

        await job.RunAsync();

        var remaining = dbContext.Set<RiderLocationPing>().Where(p => p.Id == oldPing.Id).ToList();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task RunAsync_PingWithinRetentionWindow_IsKept()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var (riderId, organizationId) = await SetupRiderAsync(dbContext);
        var recentPing = new RiderLocationPing { Id = Guid.NewGuid(), RiderId = riderId, Latitude = 27.7m, Longitude = 85.3m, RecordedAt = DateTimeOffset.UtcNow, ReceivedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        dbContext.Set<RiderLocationPing>().Add(recentPing);
        await dbContext.SaveChangesAsync();
        var job = new RiderLocationPingRetentionJob(dbContext);

        await job.RunAsync();

        var remaining = dbContext.Set<RiderLocationPing>().Where(p => p.Id == recentPing.Id).ToList();
        Assert.Single(remaining);
    }

    private static async Task<(Guid RiderId, Guid OrganizationId)> SetupRiderAsync(AppDbContext dbContext)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var organizationId = Guid.NewGuid();

        // Create geography entities
        var provinceId = Guid.NewGuid();
        var districtId = Guid.NewGuid();
        var localLevelId = Guid.NewGuid();

        dbContext.Set<Province>().Add(new Province { Id = provinceId, Name = $"Province {suffix}" });
        await dbContext.SaveChangesAsync();

        dbContext.Set<District>().Add(new District { Id = districtId, ProvinceId = provinceId, Name = $"District {suffix}" });
        await dbContext.SaveChangesAsync();

        dbContext.Set<LocalLevel>().Add(new LocalLevel { Id = localLevelId, DistrictId = districtId, Name = $"LocalLevel {suffix}", Type = LocalLevelType.Municipality });
        await dbContext.SaveChangesAsync();

        // Create organization
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = $"Test Org {suffix}", Slug = $"test-org-{suffix}" });
        await dbContext.SaveChangesAsync();

        // Create branch
        var branchId = Guid.NewGuid();
        dbContext.Set<Branch>().Add(new Branch { Id = branchId, OrganizationId = organizationId, Code = "TST-001", Name = "Test Branch", Type = BranchType.Branch, ProvinceId = provinceId, DistrictId = districtId, LocalLevelId = localLevelId, WardNumber = 1, AddressLine = "Test Address" });
        await dbContext.SaveChangesAsync();

        // Create rider
        var riderId = Guid.NewGuid();
        dbContext.Set<Rider>().Add(new Rider { Id = riderId, OrganizationId = organizationId, UserId = Guid.NewGuid(), BaseBranchId = branchId, LicenseNumber = "LIC-001", RoleType = RiderRoleType.DeliveryRider, IsActive = true });
        await dbContext.SaveChangesAsync();

        return (riderId, organizationId);
    }
}
