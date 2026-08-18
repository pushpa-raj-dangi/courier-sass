namespace Courier.Identity.Application;

public record RegisterOrganizationRequest(string OrganizationName, string OrganizationSlug, string OwnerEmail, string OwnerPassword, string OwnerFullName);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record AuthResult(string AccessToken, string RefreshToken, Guid UserId, Guid OrganizationId);

public class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException(string message) : base(message) { }
}
