// src/Modules/Riders/Application/RiderService.Location.cs
using System.Security.Claims;
using Courier.Riders.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
    // Self-scoped, same idiom as GetMyRiderAsync: false means "no Rider linked to this JWT",
    // mapped to 404 at the route (Step 5) rather than a thrown exception, since this isn't
    // invalid input — it's the same "not found" shape GetMyRiderAsync already established.
    public async Task<bool> RecordLocationPingAsync(LocationPingRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var userId = actingUser.GetUserId();
        if (userId is null)
        {
            return false;
        }
        var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);
        if (rider is null)
        {
            return false;
        }

        // Reject out-of-range coordinates/accuracy before they ever reach the database — the
        // numeric(9,6) column would otherwise throw an unhandled Postgres overflow exception on a
        // value like latitude: 1000, surfacing as a 500 instead of an actionable 400.
        if (request.Latitude is < -90 or > 90)
        {
            throw new RiderValidationException("Latitude must be between -90 and 90.");
        }
        if (request.Longitude is < -180 or > 180)
        {
            throw new RiderValidationException("Longitude must be between -180 and 180.");
        }
        if (request.AccuracyMeters is < 0)
        {
            throw new RiderValidationException("AccuracyMeters must not be negative.");
        }

        // Same find-today's-Active-run lookup GetMyTasksTodayAsync already uses — a ping made
        // before the rider's first task of the day is assigned simply has no run yet.
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var activeRun = await _dbContext.Set<DeliveryRun>().FirstOrDefaultAsync(
            r => r.RiderId == rider.Id && r.Date == today && r.Status == DeliveryRunStatus.Active, cancellationToken);

        var ping = new RiderLocationPing
        {
            Id = Guid.NewGuid(),
            RiderId = rider.Id,
            DeliveryRunId = activeRun?.Id,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            AccuracyMeters = request.AccuracyMeters,
            RecordedAt = request.RecordedAt,
            ReceivedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Set<RiderLocationPing>().Add(ping);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Per-rider latest-ping lookup — deliberately NOT a GroupBy: EF Core/Npgsql does not reliably
    // translate "GroupBy then OrderBy().First() per group" to SQL (CodService.Reads.cs hit the
    // same class of problem and resolved it by materializing a bounded set client-side; here the
    // bounded set is "riders at one branch," a small dispatch-sized number, so N small indexed
    // per-rider queries is simpler and provably correct). Sequential awaits on the shared
    // DbContext, same reasoning as GetMyTasksTodayAsync.
    public async Task<List<RiderLocationResponse>> GetBranchLocationsAsync(Guid branchId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        await CheckBranchAccessAsync(actingUser, "rider.location.read", branchId, cancellationToken);

        // branchId comes straight from the route here, not from an already tenant-scoped domain
        // entity (unlike CheckBranchAccessAsync's other call sites in this file) — an AllBranches
        // caller (e.g. OrganizationOwner) would otherwise sail through CheckBranchAccessAsync for
        // ANY branchId, including one from a different tenant, and simply see an empty result
        // instead of a rejection. Same fix BranchDashboardService.GetAsync applies for the
        // identical shape of gap: GetZoneInfoAsync returns null both when the branch doesn't
        // exist and when it belongs to another tenant (Branch's ambient OrganizationId query
        // filter excludes it either way).
        if (await _branchLookupService.GetZoneInfoAsync(branchId, cancellationToken) is null)
        {
            throw new RiderValidationException("Branch is outside your branch access for permission 'rider.location.read'.");
        }

        // Scoped by BaseBranchId, matching RiderReportingLookupService.GetBranchRiderTaskLoadAsync's
        // established convention for "riders at this branch" — not the active run's branch.
        var riderIds = await _dbContext.Set<Rider>().AsNoTracking()
            .Where(r => r.BaseBranchId == branchId && r.IsActive)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var results = new List<RiderLocationResponse>();
        foreach (var riderId in riderIds)
        {
            var latest = await _dbContext.Set<RiderLocationPing>().AsNoTracking()
                .Where(p => p.RiderId == riderId)
                .OrderByDescending(p => p.RecordedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (latest is not null)
            {
                results.Add(new RiderLocationResponse(riderId, latest.Latitude, latest.Longitude, latest.AccuracyMeters, latest.RecordedAt, latest.ReceivedAt));
            }
        }
        return results;
    }

    // DeliveryRun carries no own OrganizationId/query filter (Task 1's design note), so a raw
    // lookup by runId is NOT tenant-safe on its own — the owning Rider must be re-resolved through
    // a tenant-filtered query before trusting run.BranchId, the same way GetMyTasksTodayAsync
    // resolves Rider before ever touching DeliveryRun/DeliveryTask. Returning null for "run exists
    // but its Rider isn't visible in this tenant" (rather than a distinct error) never leaks that
    // a run with this id exists in another organization. Because the Rider lookup below is
    // tenant-filtered and DeliveryRun.BranchId is only ever set server-side from that same Rider's
    // organization, reaching CheckBranchAccessAsync below already guarantees run.BranchId belongs
    // to the caller's tenant — unlike GetBranchLocationsAsync's branchId (taken raw off the route),
    // this branchId needs no separate tenant existence check before the branch-scope check.
    public async Task<List<RiderLocationResponse>?> GetRunBreadcrumbsAsync(Guid runId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var run = await _dbContext.Set<DeliveryRun>().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return null;
        }
        var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == run.RiderId, cancellationToken);
        if (rider is null)
        {
            return null;
        }

        await CheckBranchAccessAsync(actingUser, "rider.location.read", run.BranchId, cancellationToken);

        var pings = await _dbContext.Set<RiderLocationPing>().AsNoTracking()
            .Where(p => p.DeliveryRunId == runId)
            .OrderBy(p => p.RecordedAt)
            .ToListAsync(cancellationToken);

        return pings.Select(p => new RiderLocationResponse(p.RiderId, p.Latitude, p.Longitude, p.AccuracyMeters, p.RecordedAt, p.ReceivedAt)).ToList();
    }
}
