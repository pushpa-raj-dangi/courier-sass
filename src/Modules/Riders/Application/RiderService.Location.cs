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
            RecordedAt = request.RecordedAt
        };
        _dbContext.Set<RiderLocationPing>().Add(ping);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
