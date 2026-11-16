using System.Security.Claims;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.SharedKernel;
using Courier.Shipments.Application;
using Xunit;
using static Courier.Riders.Tests.RiderReadTestSupport;

namespace Courier.Riders.Tests;

public class RiderTodayServiceTests
{
    private sealed class StubTaskBatchLookup : IShipmentTaskBatchLookupService
    {
        public Dictionary<Guid, ShipmentTaskInfo> Infos { get; } = new();
        public int Calls { get; private set; }
        public Task<IReadOnlyDictionary<Guid, ShipmentTaskInfo>> GetTaskInfosAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default)
        {
            Calls++;
            IReadOnlyDictionary<Guid, ShipmentTaskInfo> result = shipmentIds.Where(Infos.ContainsKey).ToDictionary(id => id, id => Infos[id]);
            return Task.FromResult(result);
        }
    }

    private sealed class StubCashLookup : ICodRiderCashLookupService
    {
        public Dictionary<Guid, RiderHeldCash> Held { get; } = new();
        public Task<RiderHeldCash?> GetHeldByRiderAsync(Guid riderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Held.TryGetValue(riderId, out var held) ? held : null);
    }

    private sealed class StubBranchNames : IBranchLookupService
    {
        public Dictionary<Guid, string> Names { get; } = new();
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<Guid, string> result = branchIds.Where(Names.ContainsKey).ToDictionary(id => id, id => Names[id]);
            return Task.FromResult(result);
        }
        public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

    private readonly StubTaskBatchLookup _tasks = new();
    private readonly StubCashLookup _cash = new();
    private readonly StubBranchNames _branches = new();

    private RiderTodayService CreateService(TestDbContext dbContext) => new(dbContext, _tasks, _cash, _branches);

    private static ClaimsPrincipal UserWithId(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static Rider AddRider(TestDbContext dbContext, Guid? userId = null)
    {
        var rider = new Rider
        {
            Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), UserId = userId ?? Guid.NewGuid(), BaseBranchId = Guid.NewGuid(),
            LicenseNumber = "LIC-1", RoleType = RiderRoleType.DeliveryRider, IsActive = true
        };
        dbContext.Set<Rider>().Add(rider);
        return rider;
    }

    private static DeliveryRun AddRun(TestDbContext dbContext, Rider rider, DateOnly? date = null)
    {
        var run = new DeliveryRun { Id = Guid.NewGuid(), RiderId = rider.Id, BranchId = rider.BaseBranchId, Date = date ?? Today, Status = DeliveryRunStatus.Active };
        dbContext.Set<DeliveryRun>().Add(run);
        return run;
    }

    private DeliveryTask AddTask(TestDbContext dbContext, DeliveryRun run, int sequence, DeliveryTaskStatus status, DeliveryTaskType type = DeliveryTaskType.Delivery, bool withInfo = true)
    {
        var task = new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = Guid.NewGuid(), Type = type, Sequence = sequence, Status = status };
        dbContext.Set<DeliveryTask>().Add(task);
        if (withInfo)
        {
            _tasks.Infos[task.ShipmentId] = new ShipmentTaskInfo($"AWB-{sequence}", $"Recipient {sequence}", "9800000000", "Jawalakhel", 4, sequence * 100m);
        }
        return task;
    }

    [Fact]
    public async Task GetMyTodayAsync_NoRiderForUser_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        AddRider(dbContext);
        await dbContext.SaveChangesAsync();

        Assert.Null(await CreateService(dbContext).GetMyTodayAsync(UserWithId(Guid.NewGuid())));
    }

    [Fact]
    public async Task GetMyTodayAsync_ReturnsOnlyTheCallersRunWithEveryStatusInSequence()
    {
        await using var dbContext = CreateContext();
        var userId = Guid.NewGuid();
        var me = AddRider(dbContext, userId);
        var myRun = AddRun(dbContext, me);
        AddTask(dbContext, myRun, 3, DeliveryTaskStatus.Pending);
        AddTask(dbContext, myRun, 1, DeliveryTaskStatus.Completed, DeliveryTaskType.Pickup);
        AddTask(dbContext, myRun, 2, DeliveryTaskStatus.Failed);
        var other = AddRider(dbContext);
        AddTask(dbContext, AddRun(dbContext, other), 1, DeliveryTaskStatus.Pending);
        await dbContext.SaveChangesAsync();

        var today = await CreateService(dbContext).GetMyTodayAsync(UserWithId(userId));

        Assert.NotNull(today);
        Assert.Equal(new RiderBoardRunSummary(myRun.Id, DeliveryRunStatus.Active), today!.Run);
        Assert.Equal(new[] { 1, 2, 3 }, today.Tasks.Select(t => t.Sequence).ToArray());
        Assert.Equal(new[] { DeliveryTaskStatus.Completed, DeliveryTaskStatus.Failed, DeliveryTaskStatus.Pending }, today.Tasks.Select(t => t.Status).ToArray());
        Assert.Equal("AWB-3", today.Tasks[2].Awb);
        Assert.Equal(300m, today.Tasks[2].CodAmount);
        Assert.Equal(new MyTodayCounts(3, 1, 1), today.Counts);
        Assert.Equal(1, _tasks.Calls);
    }

    [Fact]
    public async Task GetMyTodayAsync_NoRunToday_ReturnsEmptyTasksAndZeroCounts_ButStillCash()
    {
        await using var dbContext = CreateContext();
        var userId = Guid.NewGuid();
        var me = AddRider(dbContext, userId);
        AddTask(dbContext, AddRun(dbContext, me, Today.AddDays(-1)), 1, DeliveryTaskStatus.Pending);
        await dbContext.SaveChangesAsync();
        var oldest = DateTimeOffset.UtcNow.AddHours(-2);
        _cash.Held[me.Id] = new RiderHeldCash(1200m, 3, oldest);

        var today = await CreateService(dbContext).GetMyTodayAsync(UserWithId(userId));

        Assert.Null(today!.Run);
        Assert.Empty(today.Tasks);
        Assert.Equal(new MyTodayCounts(0, 0, 0), today.Counts);
        Assert.Equal(new MyCashHeld(1200m, 3, oldest), today.Cash);
        Assert.Equal(0, _tasks.Calls);
    }

    [Fact]
    public async Task GetMyTodayAsync_SkipsTasksWhoseShipmentIsMissing_AndCashNullWhenNoneHeld()
    {
        await using var dbContext = CreateContext();
        var userId = Guid.NewGuid();
        var me = AddRider(dbContext, userId);
        var run = AddRun(dbContext, me);
        AddTask(dbContext, run, 1, DeliveryTaskStatus.Pending);
        AddTask(dbContext, run, 2, DeliveryTaskStatus.Pending, withInfo: false);
        await dbContext.SaveChangesAsync();

        var today = await CreateService(dbContext).GetMyTodayAsync(UserWithId(userId));

        Assert.Single(today!.Tasks);
        Assert.Equal(new MyTodayCounts(1, 0, 0), today.Counts);
        Assert.Null(today.Cash);
    }

    [Fact]
    public async Task GetMyTodayAsync_ReturnsBranchNameAndFirstVehiclePlate()
    {
        await using var dbContext = CreateContext();
        var userId = Guid.NewGuid();
        var me = AddRider(dbContext, userId);
        dbContext.Set<Vehicle>().Add(new Vehicle { Id = Guid.NewGuid(), OrganizationId = me.OrganizationId, RiderId = me.Id, Type = "Bike", PlateNumber = "BA 3 PA 9999" });
        dbContext.Set<Vehicle>().Add(new Vehicle { Id = Guid.NewGuid(), OrganizationId = me.OrganizationId, RiderId = me.Id, Type = "Bike", PlateNumber = "BA 2 PA 1111" });
        await dbContext.SaveChangesAsync();
        _branches.Names[me.BaseBranchId] = "Lalitpur";

        var today = await CreateService(dbContext).GetMyTodayAsync(UserWithId(userId));

        Assert.Equal("Lalitpur", today!.BranchName);
        Assert.Equal("BA 2 PA 1111", today.VehiclePlate);
    }
}
