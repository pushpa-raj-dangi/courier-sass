using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class UserDirectoryLookupService : IUserDirectoryLookupService
{
    private readonly DbContext _dbContext;

    public UserDirectoryLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }
        var idArray = userIds.Distinct().ToArray();
        return await _dbContext.Set<User>().AsNoTracking()
            .Where(u => idArray.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
    }
}
