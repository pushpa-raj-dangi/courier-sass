using Courier.Branches.Domain;

namespace Courier.Branches.Application;

public record CreateBranchRequest(
    string Code,
    string Name,
    BranchType Type,
    Guid ProvinceId,
    Guid DistrictId,
    Guid LocalLevelId,
    int WardNumber,
    string AddressLine,
    string? Phone,
    string? OperatingHours,
    decimal? Latitude,
    decimal? Longitude,
    Guid? ParentBranchId,
    Guid? DeliveryZoneId = null);

public record BranchResponse(
    Guid Id,
    string Code,
    string Name,
    BranchType Type,
    Guid ProvinceId,
    Guid DistrictId,
    Guid LocalLevelId,
    int WardNumber,
    string AddressLine,
    string? Phone,
    string? OperatingHours,
    decimal? Latitude,
    decimal? Longitude,
    Guid? ParentBranchId,
    Guid? DeliveryZoneId,
    bool IsActive);

public record AssignDeliveryZoneRequest(Guid DeliveryZoneId);

public record BranchSummary(int Total, int Active, int Inactive, IReadOnlyDictionary<BranchType, int> ByType);

public class BranchValidationException : Exception
{
    public BranchValidationException(string message) : base(message) { }
}

public class BranchLimitExceededException : BranchValidationException
{
    public BranchLimitExceededException(string message) : base(message) { }
}
