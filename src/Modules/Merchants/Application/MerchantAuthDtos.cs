namespace Courier.Merchants.Application;

public record MerchantLoginRequest(string Email, string Password);
public record MerchantRefreshRequest(string RefreshToken);
public record MerchantAuthResult(string AccessToken, string RefreshToken, Guid MerchantUserId, Guid MerchantId, Guid OrganizationId);

public class MerchantAuthenticationFailedException : Exception
{
    public MerchantAuthenticationFailedException(string message) : base(message) { }
}
