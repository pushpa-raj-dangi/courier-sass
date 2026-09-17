using Courier.Branches.Domain;
using Courier.Geography.Infrastructure;
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

    // Reuses the real seeded Bagmati/Kathmandu geography (same convention as
    // CodReportingLookupServiceTests/NdrReportingLookupServiceTests) instead of inserting fresh
    // Province/District/LocalLevel rows per run — this test's DB has no reset between runs, and an
    // earlier version of this file inserted a new Province row on every execution, polluting the
    // shared Provinces table and breaking GeographyEndpointsTests' exact-count assertions.
    private static readonly Guid ProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");

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

        // Create organization
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = $"Test Org {suffix}", Slug = $"test-org-{suffix}" });
        await dbContext.SaveChangesAsync();

        // Create branch — against the real seeded Bagmati/Kathmandu geography (see ProvinceId/
        // DistrictId/LocalLevelId above), not fixture rows created by this test.
        var branchId = Guid.NewGuid();
        dbContext.Set<Branch>().Add(new Branch { Id = branchId, OrganizationId = organizationId, Code = "TST-001", Name = "Test Branch", Type = BranchType.Branch, ProvinceId = ProvinceId, DistrictId = DistrictId, LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address" });
        await dbContext.SaveChangesAsync();

        // Create rider
        var riderId = Guid.NewGuid();
        dbContext.Set<Rider>().Add(new Rider { Id = riderId, OrganizationId = organizationId, UserId = Guid.NewGuid(), BaseBranchId = branchId, LicenseNumber = "LIC-001", RoleType = RiderRoleType.DeliveryRider, IsActive = true });
        await dbContext.SaveChangesAsync();

        return (riderId, organizationId);
    }
}
