namespace Courier.Merchants.Application;

public interface IMerchantJwtTokenService
{
    string IssueAccessToken(Guid merchantUserId, Guid merchantId, Guid organizationId, string email);
}
