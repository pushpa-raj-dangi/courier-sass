namespace Courier.SharedKernel;

public interface ICurrentTenantProvider
{
    Guid? OrganizationId { get; }
}
