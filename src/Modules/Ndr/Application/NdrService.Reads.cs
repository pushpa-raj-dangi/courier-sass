using Courier.Ndr.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public partial class NdrService
{
    public async Task<NdrResponse?> GetByIdAsync(Guid ndrId, CancellationToken cancellationToken = default)
    {
        var ndr = await _dbContext.Set<NDR>().FirstOrDefaultAsync(n => n.Id == ndrId, cancellationToken);
        return ndr is null ? null : ToResponse(ndr);
    }

    public async Task<List<NdrResponse>> ListAsync(NdrStatus? status, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<NDR>().AsQueryable();
        if (status is not null)
        {
            query = query.Where(n => n.Status == status);
        }

        return await query
            .Select(n => new NdrResponse(n.Id, n.ShipmentId, n.Reason, n.AssignedStaffUserId, n.Status, n.CustomerResponse, n.FollowUpAt, n.NextAction, n.Resolution, n.ResolvedAt))
            .ToListAsync(cancellationToken);
    }
}
