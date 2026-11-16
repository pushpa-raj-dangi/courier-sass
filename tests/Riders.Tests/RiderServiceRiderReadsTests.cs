using Courier.Riders.Application;
using Courier.Riders.Domain;
using Xunit;
using static Courier.Riders.Tests.RiderReadTestSupport;

namespace Courier.Riders.Tests;

public class RiderServiceRiderReadsTests
{
    private static Rider AddRider(TestDbContext dbContext, Guid branchId)
    {
        var rider = new Rider
        {
            Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), UserId = Guid.NewGuid(), BaseBranchId = branchId,
            LicenseNumber = "LIC-7", RoleType = RiderRoleType.DeliveryRider, IsActive = true
        };
        dbContext.Set<Rider>().Add(rider);
        return rider;
    }

    private static DeliveryRun AddActiveRun(TestDbContext dbContext, Rider rider)
    {
        var run = new DeliveryRun
        {
            Id = Guid.NewGuid(), RiderId = rider.Id, BranchId = rider.BaseBranchId,
            Date = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date), Status = DeliveryRunStatus.Active
        };
        dbContext.Set<DeliveryRun>().Add(run);
        return run;
    }

    private static DeliveryTask AddTask(TestDbContext dbContext, DeliveryRun run, int sequence, DeliveryTaskStatus status, DeliveryTaskType type = DeliveryTaskType.Delivery)
    {
        var task = new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = Guid.NewGuid(), Type = type, Sequence = sequence, Status = status };
        dbContext.Set<DeliveryTask>().Add(task);
        return task;
    }

    [Fact]
    public async Task GetByIdAsync_InScope_ReturnsRider()
    {
        await using var dbContext = CreateContext();
        var branch = Guid.NewGuid();
        var rider = AddRider(dbContext, branch);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new HashSet<Guid> { branch });

        var result = await service.GetByIdAsync(rider.Id, ActingUser());

        Assert.Equal("LIC-7", result!.LicenseNumber);
    }

    [Fact]
    public async Task GetByIdAsync_OutOfScope_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var rider = AddRider(dbContext, Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new HashSet<Guid> { Guid.NewGuid() });

        Assert.Null(await service.GetByIdAsync(rider.Id, ActingUser()));
    }

    [Fact]
    public async Task GetTodayAsync_UnknownRider_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, null);

        Assert.Null(await service.GetTodayAsync(Guid.NewGuid(), ActingUser()));
    }

    [Fact]
    public async Task GetTodayAsync_OutOfScope_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var rider = AddRider(dbContext, Guid.NewGuid());
        AddTask(dbContext, AddActiveRun(dbContext, rider), 1, DeliveryTaskStatus.Pending);
        await dbContext.SaveChangesAsync();
        var shipments = new StubShipmentReportingLookup();
        var service = CreateService(dbContext, new HashSet<Guid> { Guid.NewGuid() }, shipments);

        Assert.Null(await service.GetTodayAsync(rider.Id, ActingUser()));
        Assert.Equal(0, shipments.AwbLookupCalls);
    }

    [Fact]
    public async Task GetTodayAsync_ReturnsTasksBySequenceWithOneAwbLookup()
    {
        await using var dbContext = CreateContext();
        var rider = AddRider(dbContext, Guid.NewGuid());
        var run = AddActiveRun(dbContext, rider);
        var third = AddTask(dbContext, run, 3, DeliveryTaskStatus.Pending);
        var first = AddTask(dbContext, run, 1, DeliveryTaskStatus.Completed, DeliveryTaskType.Pickup);
        var second = AddTask(dbContext, run, 2, DeliveryTaskStatus.Failed);
        await dbContext.SaveChangesAsync();
        var shipments = new StubShipmentReportingLookup();
        shipments.Awbs[first.ShipmentId] = "AWB-001";
        shipments.Awbs[third.ShipmentId] = "AWB-003";
        var service = CreateService(dbContext, null, shipments);

        var today = await service.GetTodayAsync(rider.Id, ActingUser());

        Assert.NotNull(today);
        Assert.Equal(rider.Id, today!.Rider.RiderId);
        Assert.Equal(3, today.Rider.TasksAssigned);
        Assert.Equal(new[] { 1, 2, 3 }, today.Tasks.Select(t => t.Sequence).ToArray());
        Assert.Equal(new[] { "AWB-001", "", "AWB-003" }, today.Tasks.Select(t => t.Awb).ToArray());
        Assert.Equal(DeliveryTaskType.Pickup, today.Tasks[0].Type);
        Assert.Equal(DeliveryTaskStatus.Failed, today.Tasks[1].Status);
        Assert.Equal(second.Id, today.Tasks[1].TaskId);
        Assert.Equal(1, shipments.AwbLookupCalls);
    }

    [Fact]
    public async Task GetTodayAsync_NoRun_ReturnsRowAndNoTasksWithoutAwbLookup()
    {
        await using var dbContext = CreateContext();
        var rider = AddRider(dbContext, Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var shipments = new StubShipmentReportingLookup();
        var service = CreateService(dbContext, null, shipments);

        var today = await service.GetTodayAsync(rider.Id, ActingUser());

        Assert.Null(today!.Rider.Run);
        Assert.Empty(today.Tasks);
        Assert.Equal(0, shipments.AwbLookupCalls);
    }
}
