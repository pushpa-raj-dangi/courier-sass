using Courier.Merchants.Domain;

namespace Courier.Merchants.Application;

public record MerchantLoginRequest(string Email, string Password);
public record MerchantRefreshRequest(string RefreshToken);
public record MerchantAuthResult(string AccessToken, string RefreshToken, Guid MerchantUserId, Guid MerchantId, Guid OrganizationId);

// Mirrors Identity.Application.AuthDtos.MeResponse's shape and purpose for the merchant track —
// the one call the frontend session hook needs to know who is logged in, their role, and their
// resolved permission set (added Frontend Phase F10).
public record MerchantMeResponse(
    Guid MerchantUserId, Guid MerchantId, Guid OrganizationId,
    string MerchantDisplayName, string Email, string FullName,
    MerchantUserRole Role, IReadOnlyList<string> Permissions, KycStatus KycStatus);

public class MerchantAuthenticationFailedException : Exception
{
    public MerchantAuthenticationFailedException(string message) : base(message) { }
}

public class MerchantOrganizationSuspendedException : Exception
{
    public MerchantOrganizationSuspendedException(string message) : base(message) { }
}
