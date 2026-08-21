using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class OrganizationLookupService : IOrganizationLookupService
{
    private readonly DbContext _dbContext;

    public OrganizationLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        (await _dbContext.Set<Organization>().FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken))?.Slug;
}
