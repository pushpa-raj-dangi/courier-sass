using Courier.Branches.Domain;

namespace Courier.Branches.Application;

public record CreateBranchRequest(
    string Code,
    string Name,
    BranchType Type,
    string AddressLine,
    string? Phone,
    string? OperatingHours,
    decimal? Latitude,
    decimal? Longitude,
    Guid? ParentBranchId);

public record BranchResponse(
    Guid Id,
    string Code,
    string Name,
    BranchType Type,
    string AddressLine,
    string? Phone,
    string? OperatingHours,
    decimal? Latitude,
    decimal? Longitude,
    Guid? ParentBranchId,
    bool IsActive);

public class BranchValidationException : Exception
{
    public BranchValidationException(string message) : base(message) { }
}
