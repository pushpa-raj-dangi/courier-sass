namespace Courier.Merchants.Application;

public interface IMerchantSessionRevoker
{
    Task RevokeAllForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
