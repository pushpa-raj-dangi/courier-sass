namespace Courier.Identity.Application;

// Read-only display-name lookup for other modules (F15 R3b-2: the rider board). Operations roles
// lack user.read, so rider names are resolved server-side here instead of through /users. Tenant
// scope is the User query filter: ids from another organization are simply absent from the result.
public interface IUserDirectoryLookupService
{
    Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
}
