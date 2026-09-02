namespace Courier.Identity.Domain;

// Mirrors RefreshToken's shape: a random plaintext token is emailed to the invitee and never
// stored; only its SHA-256 hash lives here. UserId points at a User row already created (with an
// unusable placeholder PasswordHash) at invite time — see UserManagementService.InviteUserAsync.
public class UserInvite
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}
