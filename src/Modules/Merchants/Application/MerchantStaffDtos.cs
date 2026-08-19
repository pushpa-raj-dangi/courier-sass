using Courier.Merchants.Domain;

namespace Courier.Merchants.Application;

public record InviteMerchantStaffRequest(string Email, string Password, string FullName);
public record MerchantStaffResponse(Guid Id, string Email, string FullName, MerchantUserRole Role, bool IsActive);
