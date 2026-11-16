using Courier.Riders.Application;
using Courier.Riders.Domain;
using Xunit;
using static Courier.Riders.Tests.RiderReadTestSupport;

namespace Courier.Riders.Tests;

public class RiderServiceBoardTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

    private static Rider AddRider(TestDbContext dbContext, Guid branchId, bool isActive = true, RiderRoleType roleType = RiderRoleType.DeliveryRider)
    {
        var rider = new Rider
        {
            Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), UserId = Guid.NewGuid(), BaseBranchId = branchId,
            LicenseNumber = $"LIC-{Guid.NewGuid():N}"[..12], RoleType = roleType, IsActive = isActive
        };
        dbContext.Set<Rider>().Add(rider);
        return rider;
    }

    private static DeliveryRun AddRun(TestDbContext dbContext, Rider rider, DeliveryRunStatus status, DateOnly? date = null)
    {
        var run = new DeliveryRun { Id = Guid.NewGuid(), RiderId = rider.Id, BranchId = rider.BaseBranchId, Date = date ?? Today, Status = status };
        dbContext.Set<DeliveryRun>().Add(run);
        return run;
    }

    private static void AddTask(TestDbContext dbContext, DeliveryRun run, int sequence, DeliveryTaskStatus status) =>
        dbContext.Set<DeliveryTask>().Add(new DeliveryTask
        {
            Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = Guid.NewGuid(), Type = DeliveryTaskType.Delivery,
            Sequence = sequence, Status = status
        });

    private static void AddPing(TestDbContext dbContext, Rider rider, DateTimeOffset receivedAt) =>
        dbContext.Set<RiderLocationPing>().Add(new RiderLocationPing
        {
            Id = Guid.NewGuid(), RiderId = rider.Id, Latitude = 27.7m, Longitude = 85.3m, AccuracyMeters = 8m,
            RecordedAt = receivedAt, ReceivedAt = receivedAt
        });

    [Fact]
    public async Task GetBoardAsync_ScopedUser_ReturnsOnlyRidersBasedInScope()
    {
        await using var dbContext = CreateContext();
        var visibleBranch = Guid.NewGuid();
        var visible = AddRider(dbContext, visibleBranch);
        AddRider(dbContext, Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new HashSet<Guid> { visibleBranch });

        var board = await service.GetBoardAsync(null, ActingUser());

        Assert.Equal(visible.Id, Assert.Single(board.Items).RiderId);
    }

    [Fact]
    public async Task GetBoardAsync_BranchIdOutsideScope_ReturnsEmpty()
    {
        await using var dbContext = CreateContext();
        var visibleBranch = Guid.NewGuid();
        var hiddenBranch = Guid.NewGuid();
        AddRider(dbContext, visibleBranch);
        AddRider(dbContext, hiddenBranch);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new HashSet<Guid> { visibleBranch });

        var board = await service.GetBoardAsync(hiddenBranch, ActingUser());

        Assert.Empty(board.Items);
    }

    [Fact]
    public async Task GetBoardAsync_BranchIdNarrowsAllBranchesScope()
    {
        await using var dbContext = CreateContext();
        var branchA = Guid.NewGuid();
        var riderA = AddRider(dbContext, branchA);
        AddRider(dbContext, Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, null);

        var board = await service.GetBoardAsync(branchA, ActingUser());

        Assert.Equal(riderA.Id, Assert.Single(board.Items).RiderId);
    }

    [Fact]
    public async Task GetBoardAsync_NoRunToday_RowHasNoRunAndZeroCounts()
    {
        await using var dbContext = CreateContext();
        var rider = AddRider(dbContext, Guid.NewGuid());
        var yesterdaysRun = AddRun(dbContext, rider, DeliveryRunStatus.Active, Today.AddDays(-1));
        AddTask(dbContext, yesterdaysRun, 1, DeliveryTaskStatus.Pending);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, null);

        var row = Assert.Single((await service.GetBoardAsync(null, ActingUser())).Items);

        Assert.Null(row.Run);
        Assert.Equal(0, row.TasksAssigned);
        Assert.Equal(0, row.TasksCompleted);
        Assert.Equal(0, row.TasksFailed);
    }

    [Fact]
    public async Task GetBoardAsync_ActiveRun_CountsTodaysTasksByStatus()
    {
        await using var dbContext = CreateContext();
        var rider = AddRider(dbContext, Guid.NewGuid());
        var run = AddRun(dbContext, rider, DeliveryRunStatus.Active);
        AddTask(dbContext, run, 1, DeliveryTaskStatus.Completed);
        AddTask(dbContext, run, 2, DeliveryTaskStatus.Completed);
        AddTask(dbContext, run, 3, DeliveryTaskStatus.Failed);
        AddTask(dbContext, run, 4, DeliveryTaskStatus.Pending);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, null);

        var row = Assert.Single((await service.GetBoardAsync(null, ActingUser())).Items);

        Assert.Equal(new RiderBoardRunSummary(run.Id, DeliveryRunStatus.Active), row.Run);
        Assert.Equal(4, row.TasksAssigned);
        Assert.Equal(2, row.TasksCompleted);
        Assert.Equal(1, row.TasksFailed);
    }

    [Fact]
    public async Task GetBoardAsync_CompletedAndActiveRunsToday_PrefersActive()
    {
        await using var dbContext = CreateContext();
        var onlyCompleted = AddRider(dbContext, Guid.NewGuid());
        var completedRun = AddRun(dbContext, onlyCompleted, DeliveryRunStatus.Completed);
        var both = AddRider(dbContext, Guid.NewGuid());
        AddRun(dbContext, both, DeliveryRunStatus.Completed);
        var activeRun = AddRun(dbContext, both, DeliveryRunStatus.Active);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, null);

        var items = (await service.GetBoardAsync(null, ActingUser())).Items;

        Assert.Equal(new RiderBoardRunSummary(completedRun.Id, DeliveryRunStatus.Completed), items.Single(r => r.RiderId == onlyCompleted.Id).Run);
        Assert.Equal(new RiderBoardRunSummary(activeRun.Id, DeliveryRunStatus.Active), items.Single(r => r.RiderId == both.Id).Run);
    }

    [Fact]
    public async Task GetBoardAsync_ReportsLatestPingToday_AndNullWhenOnlyYesterday()
    {
        await using var dbContext = CreateContext();
        var pinged = AddRider(dbContext, Guid.NewGuid());
        var latest = DateTimeOffset.UtcNow;
        AddPing(dbContext, pinged, latest.AddSeconds(-1));
        AddPing(dbContext, pinged, latest);
        var yesterdayOnly = AddRider(dbContext, Guid.NewGuid());
        AddPing(dbContext, yesterdayOnly, DateTimeOffset.UtcNow.AddDays(-1));
        var never = AddRider(dbContext, Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, null);

        var items = (await service.GetBoardAsync(null, ActingUser())).Items;

        Assert.Equal(latest, items.Single(r => r.RiderId == pinged.Id).LastPingAt);
        Assert.Null(items.Single(r => r.RiderId == yesterdayOnly.Id).LastPingAt);
        Assert.Null(items.Single(r => r.RiderId == never.Id).LastPingAt);
    }

    [Fact]
    public async Task GetBoardAsync_VehiclePlate_FirstByPlateNumber_NullWhenNone()
    {
        await using var dbContext = CreateContext();
        var withTwo = AddRider(dbContext, Guid.NewGuid());
        dbContext.Set<Vehicle>().Add(new Vehicle { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), RiderId = withTwo.Id, Type = "Bike", PlateNumber = "BA 3 PA 9999" });
        dbContext.Set<Vehicle>().Add(new Vehicle { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), RiderId = withTwo.Id, Type = "Bike", PlateNumber = "BA 2 PA 1111" });
        var without = AddRider(dbContext, Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, null);

        var items = (await service.GetBoardAsync(null, ActingUser())).Items;

        Assert.Equal("BA 2 PA 1111", items.Single(r => r.RiderId == withTwo.Id).VehiclePlate);
        Assert.Null(items.Single(r => r.RiderId == without.Id).VehiclePlate);
    }

    [Fact]
    public async Task GetBoardAsync_NamesFromDirectory_FallBackToRider()
    {
        await using var dbContext = CreateContext();
        var named = AddRider(dbContext, Guid.NewGuid());
        var unnamed = AddRider(dbContext, Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var names = new StubUserDirectoryLookup();
        names.Names[named.UserId] = "Asha Gurung";
        var service = CreateService(dbContext, null, names: names);

        var items = (await service.GetBoardAsync(null, ActingUser())).Items;

        Assert.Equal("Asha Gurung", items.Single(r => r.RiderId == named.Id).FullName);
        Assert.Equal("Rider", items.Single(r => r.RiderId == unnamed.Id).FullName);
    }

    [Fact]
    public async Task GetBoardAsync_IncludesInactiveRiders()
    {
        await using var dbContext = CreateContext();
        var inactive = AddRider(dbContext, Guid.NewGuid(), isActive: false);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, null);

        var row = Assert.Single((await service.GetBoardAsync(null, ActingUser())).Items);

        Assert.Equal(inactive.Id, row.RiderId);
        Assert.False(row.IsActive);
    }

    [Fact]
    public async Task GetBoardAsync_OrdersOnRunThenRunDoneThenOffDuty_ThenByName()
    {
        await using var dbContext = CreateContext();
        var branch = Guid.NewGuid();
        var offDuty = AddRider(dbContext, branch);
        var done = AddRider(dbContext, branch);
        AddRun(dbContext, done, DeliveryRunStatus.Completed);
        var onRunZed = AddRider(dbContext, branch);
        AddRun(dbContext, onRunZed, DeliveryRunStatus.Active);
        var onRunAsha = AddRider(dbContext, branch);
        AddRun(dbContext, onRunAsha, DeliveryRunStatus.Active);
        await dbContext.SaveChangesAsync();
        var names = new StubUserDirectoryLookup();
        names.Names[offDuty.UserId] = "Aaron";
        names.Names[done.UserId] = "Bina";
        names.Names[onRunZed.UserId] = "Zed";
        names.Names[onRunAsha.UserId] = "asha";
        var service = CreateService(dbContext, null, names: names);

        var items = (await service.GetBoardAsync(null, ActingUser())).Items;

        Assert.Equal(new[] { "asha", "Zed", "Bina", "Aaron" }, items.Select(r => r.FullName).ToArray());
    }
}
