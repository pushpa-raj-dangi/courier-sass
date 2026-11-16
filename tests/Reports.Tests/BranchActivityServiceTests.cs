using System.Security.Claims;
using Courier.Reports.Application;
using Courier.Shipments.Application;

namespace Courier.Reports.Tests;

public class BranchActivityServiceTests
{
    private static ClaimsPrincipal AnyUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    private readonly FakeShipmentReportingLookupService _shipments = new();
    private readonly FakeRiderReportingLookupService _riders = new();
    private readonly FakeNdrReportingLookupService _ndrs = new();
    private readonly FakeBranchAuthorizationChecker _checker = new();

    private BranchActivityService CreateService() => new(_shipments, _riders, _ndrs, _checker);

    [Fact]
    public async Task GetAsync_AllBranchesScope_MergesSparseRowsFromEveryLookup()
    {
        var busy = Guid.NewGuid();
        var ridersOnly = Guid.NewGuid();
        var ndrOnly = Guid.NewGuid();
        _shipments.CustodyCounts = new Dictionary<Guid, BranchCustodyCounts> { [busy] = new(12, 4) };
        _riders.RidersOutCounts = new Dictionary<Guid, int> { [busy] = 3, [ridersOnly] = 1 };
        _ndrs.OpenNdrCountsByBranch = new Dictionary<Guid, int> { [busy] = 2, [ndrOnly] = 5 };

        var result = await CreateService().GetAsync(AnyUser());

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(new BranchActivityRow(busy, 12, 4, 3, 2), result.Items.Single(r => r.BranchId == busy));
        Assert.Equal(new BranchActivityRow(ridersOnly, 0, 0, 1, 0), result.Items.Single(r => r.BranchId == ridersOnly));
        Assert.Equal(new BranchActivityRow(ndrOnly, 0, 0, 0, 5), result.Items.Single(r => r.BranchId == ndrOnly));
        Assert.Equal(result.Items.Select(r => r.BranchId).OrderBy(id => id), result.Items.Select(r => r.BranchId));
        Assert.Null(_shipments.LastCustodyBranchIds);
    }

    [Fact]
    public async Task GetAsync_ScopedUser_OneRowPerBranchInScopeIncludingZeros_AndScopeForwarded()
    {
        var quiet = Guid.NewGuid();
        var active = Guid.NewGuid();
        var scope = new HashSet<Guid> { quiet, active };
        _checker.AccessibleBranchIds = scope;
        _riders.RidersOutCounts = new Dictionary<Guid, int> { [active] = 2 };

        var result = await CreateService().GetAsync(AnyUser());

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(new BranchActivityRow(quiet, 0, 0, 0, 0), result.Items.Single(r => r.BranchId == quiet));
        Assert.Equal(2, result.Items.Single(r => r.BranchId == active).RidersOut);
        Assert.Same(scope, _shipments.LastCustodyBranchIds);
        Assert.Same(scope, _riders.LastRidersOutBranchIds);
        Assert.Same(scope, _ndrs.LastOpenNdrBranchIds);
    }

    [Fact]
    public async Task GetAsync_PassesTodaysUtcDateToRidersOut()
    {
        var before = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        await CreateService().GetAsync(AnyUser());

        var after = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        Assert.InRange(_riders.LastRidersOutToday!.Value, before, after);
    }
}
