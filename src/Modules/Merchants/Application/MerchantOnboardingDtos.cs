using Courier.Merchants.Domain;

namespace Courier.Merchants.Application;

public record CreateMerchantRequest(
    string LegalName,
    string DisplayName,
    string? ContactPhone,
    string? ContactEmail,
    string OwnerEmail,
    string OwnerPassword,
    string OwnerFullName,
    Guid ProvinceId,
    Guid DistrictId,
    Guid LocalLevelId,
    int WardNumber,
    string AddressLine);

public record MerchantResponse(
    Guid Id,
    string LegalName,
    string DisplayName,
    string? ContactPhone,
    string? ContactEmail,
    KycStatus KycStatus,
    bool IsActive);

public class MerchantValidationException : Exception
{
    public MerchantValidationException(string message) : base(message) { }
}
